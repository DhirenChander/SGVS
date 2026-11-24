using System.Diagnostics;

namespace SGVS.Frontend.Services.Video;

// ffmpeg decoding an H.264 stream from stdin to raw BGRA frames on stdout.
// -flags low_delay keeps it about two frames behind the input. do NOT add -fflags nobuffer: measured with
// ffmpeg 8.0.1 it dropped 59 of 150 frames
public sealed class FfmpegVideoDecoder : IVideoDecoder
{
    private static readonly string[] Args =
    [
        "-hide_banner", "-loglevel", "error",
        "-probesize", "32", "-analyzeduration", "0", "-flags", "low_delay",
        "-f", "h264", "-i", "pipe:0",
        "-fps_mode", "passthrough",
        "-vf", $"scale={VideoFormat.Width}:{VideoFormat.Height}",
        "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1",
    ];

    private readonly Process _process;
    private readonly Stream _stdin;
    private readonly Thread _reader;
    private readonly object _writeLock = new();
    private volatile bool _dead;
    private bool _disposed;

    public event Action<byte[]>? FrameDecoded;

    public FfmpegVideoDecoder(string ffmpegPath)
    {
        _process = Ffmpeg.Start(ffmpegPath, Args, writesStdin: true);
        _stdin = _process.StandardInput.BaseStream;
        var stdout = _process.StandardOutput.BaseStream;
        _reader = new Thread(() => ReadLoop(stdout)) { IsBackground = true, Name = "Stream decoder" };
        _reader.Start();
    }

    public void Decode(ReadOnlySpan<byte> accessUnit)
    {
        lock (_writeLock)
        {
            if (_dead || _disposed) return;
            try
            {
                _stdin.Write(accessUnit);
                _stdin.Flush();
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // ffmpeg is gone, so drop this and everything after it
                _dead = true;
            }
        }
    }

    private void ReadLoop(Stream stdout)
    {
        var frame = new byte[VideoFormat.FrameBytes];
        try
        {
            while (true)
            {
                stdout.ReadExactly(frame, 0, frame.Length);
                FrameDecoded?.Invoke(frame);
            }
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // output ended: the input was closed or ffmpeg died
        }
        finally
        {
            _dead = true;
        }
    }

    // lets a test make the process die underneath the decoder
    internal void KillForTest()
    {
        try { _process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        _process.WaitForExit(2000);
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        try { _stdin.Close(); } // ends the input so ffmpeg flushes the frames it still holds
        catch (Exception e) when (e is IOException or ObjectDisposedException) { }

        if (_reader != Thread.CurrentThread && !_reader.Join(TimeSpan.FromSeconds(2)))
        {
            try { _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            _reader.Join(TimeSpan.FromSeconds(2));
        }

        _process.Dispose();
    }
}
