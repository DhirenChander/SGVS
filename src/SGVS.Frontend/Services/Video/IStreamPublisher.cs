namespace SGVS.Frontend.Services.Video;

// where a stream we send comes from: raises one H.264 access unit per frame. the dummy video is one
// implementation, live capture will be another
public interface IStreamPublisher : IDisposable
{
    // raised on a background thread, the receiver owns the array
    event Action<byte[]>? AccessUnitEncoded;

    // the stream ended without Stop being called: the encoder died, or the input ran out
    event Action<string>? Failed;

    void Start();

    void Stop();
}
