namespace SGVS.Frontend.Services.Video;

// turns H.264 access units into pictures
public interface IVideoDecoder : IDisposable
{
    // one BGRA frame of exactly VideoFormat.FrameBytes bytes, raised on the decoder's own thread.
    // the buffer is reused for the next frame, so copy it if it is needed after the call returns
    event Action<byte[]>? FrameDecoded;

    // the decoder stopped on its own (its process died), not through Dispose. raised once, on the decoder's thread
    event Action? Failed;

    // feed one access unit. never throws: if the decoder has died the access unit is dropped
    void Decode(ReadOnlySpan<byte> accessUnit);
}
