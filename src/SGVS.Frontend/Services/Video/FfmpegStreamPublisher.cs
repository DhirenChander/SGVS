using System.Diagnostics;

namespace SGVS.Frontend.Services.Video;

// ffmpeg encoding `inputArgs` to a live H.264 stream: 1280x720, 30 fps, 3000 kbps CBR, a keyframe every second,
// no B-frames, SPS/PPS repeated on every keyframe, and an access unit delimiter in front of every frame
// (which is what AnnexBAccessUnitSplitter cuts on)
public sealed class FfmpegStreamPublisher(string ffmpegPath, IReadOnlyList<string> inputArgs) : IStreamPublisher
{
    public static IReadOnlyList<string> EncodeArgs { get; } =
    [
        "-an",
        "-vf", $"scale={VideoFormat.Width}:{VideoFormat.Height},fps={VideoFormat.FrameRate}",
        "-c:v", "libx264", "-preset", "veryfast", "-tune", "zerolatency",
        // constrained baseline level 3.1 is what pion's default H.264 line (profile-level-id=42e01f) accepts
        "-profile:v", "baseline", "-level", "3.1", "-pix_fmt", "yuv420p",
        "-g", VideoFormat.Gop.ToString(), "-bf", "0",
        "-b:v", $"{VideoFormat.BitrateKbps}k", "-maxrate", $"{VideoFormat.BitrateKbps}k", "-bufsize", $"{VideoFormat.BitrateKbps / 2}k",
        "-x264-params", "aud=1:repeat-headers=1",
        "-f", "h264", "pipe:1",
    ];

    private const int ReadChunkBytes = 64 * 1024;
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(2);

    private readonly object _lock = new();
    private Process? _process;
    private Thread? _reader;
    private volatile bool _stopping;
    private volatile string? _lastError; // ffmpeg's last stderr line, -loglevel error keeps that to real errors

    public event Action<byte[]>? AccessUnitEncoded;
    public event Action<string>? Failed;

    // the Rocket League clip, looped forever and paced to real time. throws InvalidOperationException with a
    // message fit for the screen when ffmpeg or the clip is missing
    public static IStreamPublisher CreateDummy()
    {
        var ffmpeg = Ffmpeg.Require();

        var video = Path.Combine(AppContext.BaseDirectory, "StreamVideo", "rocket-league.mp4");
        if (!File.Exists(video))
            throw new InvalidOperationException("The dummy stream video is missing. Run scripts\\get-dummy-video.ps1 and rebuild.");

        return new FfmpegStreamPublisher(ffmpeg, ["-re", "-stream_loop", "-1", "-i", video]);
    }

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                if (_stopping || _process is null) return false;
                try { return !_process.HasExited; }
                catch (InvalidOperationException) { return false; }
            }
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_process is not null) return;
            _stopping = false;

            string[] head = ["-hide_banner", "-loglevel", "error", "-nostdin"];
            _lastError = null;
            var process = Ffmpeg.Start(ffmpegPath, [.. head, .. inputArgs, .. EncodeArgs], writesStdin: false,
                onStderr: line => _lastError = line);
            _process = process;
            // taken here, under the lock: Stop can dispose the process, after which StandardOutput throws
            var stdout = process.StandardOutput.BaseStream;
            _reader = new Thread(() => ReadLoop(process, stdout)) { IsBackground = true, Name = "Stream encoder" };
            _reader.Start();
        }
    }

    private void ReadLoop(Process process, Stream stdout)
    {
        var splitter = new AnnexBAccessUnitSplitter();
        var chunk = new byte[ReadChunkBytes];
        try
        {
            int read;
            while ((read = stdout.Read(chunk, 0, chunk.Length)) > 0)
            {
                foreach (var au in splitter.Push(chunk.AsSpan(0, read)))
                    AccessUnitEncoded?.Invoke(au);
            }

            if (!_stopping && splitter.Flush() is { } last)
                AccessUnitEncoded?.Invoke(last);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // the pipe closed under us, same as the end of the output
        }

        if (_stopping) return;
        WaitForErrorOutput(process);
        if (_stopping) return;
        Failed?.Invoke(_lastError is { } error ? $"The stream encoder stopped unexpectedly: {error}" : "The stream encoder stopped unexpectedly.");
    }

    // stderr is read on its own, so it can still be on its way when stdout ends. only the parameterless WaitForExit
    // waits for it, and only once the process has exited
    private static void WaitForErrorOutput(Process process)
    {
        try
        {
            if (process.WaitForExit(ExitWait)) process.WaitForExit();
        }
        catch (InvalidOperationException)
        {
            // Stop disposed the process meanwhile
        }
    }

    public void Stop()
    {
        Process? process;
        Thread? reader;
        lock (_lock)
        {
            _stopping = true;
            process = _process;
            reader = _reader;
            _process = null;
            _reader = null;
        }

        if (process is null) return;
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { } // already exited
        if (reader is not null && reader != Thread.CurrentThread) reader.Join(ExitWait);
        process.Dispose();
    }

    public void Dispose() => Stop();
}
