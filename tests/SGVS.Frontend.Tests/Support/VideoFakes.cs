using SGVS.Frontend.Services.Video;

namespace SGVS.Frontend.Tests.Support;

// a decoder that remembers what it was fed. Raise plays the part of ffmpeg producing a picture
public sealed class FakeVideoDecoder : IVideoDecoder
{
    private readonly List<byte[]> _received = [];

    public event Action<byte[]>? FrameDecoded;
    public event Action? Failed;

    public bool Disposed { get; private set; }
    public bool ThrowOnDecode { get; set; }

    // the decoder's process died on its own
    public void Die() => Failed?.Invoke();

    public byte[][] Received
    {
        get { lock (_received) return [.. _received]; }
    }

    public void Decode(ReadOnlySpan<byte> accessUnit)
    {
        if (ThrowOnDecode) throw new InvalidOperationException("decoder broke");
        lock (_received) _received.Add(accessUnit.ToArray());
    }

    public void Raise(byte[] frame) => FrameDecoded?.Invoke(frame);

    public void Dispose() => Disposed = true;
}

public sealed class RecordingSink : IVideoFrameSink
{
    private readonly List<byte[]> _frames = [];

    public bool Disposed { get; private set; }

    public byte[][] Frames
    {
        get { lock (_frames) return [.. _frames]; }
    }

    public void Present(ReadOnlySpan<byte> bgra)
    {
        lock (_frames) _frames.Add(bgra.ToArray());
    }

    public void Dispose() => Disposed = true;
}

// stands in for the dummy encoder: Emit and Fail are what ffmpeg would do
public sealed class FakeStreamPublisher : IStreamPublisher
{
    public event Action<byte[]>? AccessUnitEncoded;
    public event Action<string>? Failed;

    public bool Started { get; private set; }
    public bool Disposed { get; private set; }

    // makes Start fail the way Process.Start does when ffmpeg is not a runnable program
    public Exception? StartError { get; set; }

    public void Start()
    {
        if (StartError is not null) throw StartError;
        Started = true;
    }

    public void Stop() => Disposed = true;

    public void Emit(byte[] accessUnit) => AccessUnitEncoded?.Invoke(accessUnit);

    public void Fail(string message) => Failed?.Invoke(message);

    public void Dispose() => Disposed = true;
}
