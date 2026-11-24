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
    private int _disposed; // 1 once Dispose has started. not guarded by _writeLock, a stuck Decode can hold that

    public event Action<byte[]>? FrameDecoded;
    public event Action? Failed;

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
            if (_dead || Volatile.Read(ref _disposed) == 1) return;
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

        // Dispose sets _disposed before it closes the input, so an output that ended because of Dispose is not a failure
        if (Volatile.Read(ref _disposed) == 0) Failed?.Invoke();
    }

    // lets a test make the process die underneath the decoder
    internal void KillForTest()
    {
        try { _process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        _process.WaitForExit(2000);
    }

    // a Decode still writing after a short wait is stuck on an ffmpeg that stopped reading its input. waiting for it
    // would hang whoever is disposing (the UI thread, on Leave), so ffmpeg is killed first, which fails that write
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        var gotLock = Monitor.TryEnter(_writeLock, TimeSpan.FromMilliseconds(500));
        if (!gotLock) Kill();
        try
        {
            _stdin.Close(); // ends the input so ffmpeg flushes the frames it still holds
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { }
        finally
        {
            if (gotLock) Monitor.Exit(_writeLock);
        }

        if (_reader != Thread.CurrentThread && !_reader.Join(TimeSpan.FromSeconds(2)))
        {
            Kill();
            _reader.Join(TimeSpan.FromSeconds(2));
        }

        _process.Dispose();
    }

    private void Kill()
    {
        try { _process.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { } // already exited
    }
}
