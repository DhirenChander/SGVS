namespace SGVS.Frontend.Services.Video;

// where decoded pictures go
public interface IVideoFrameSink : IDisposable
{
    // one BGRA frame of VideoFormat.FrameBytes bytes, called on the decoder's thread.
    // bgra is only valid during the call, the decoder reuses its buffer: copy it
    void Present(ReadOnlySpan<byte> bgra);
}
