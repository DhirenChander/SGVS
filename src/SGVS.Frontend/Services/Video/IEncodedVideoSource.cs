namespace SGVS.Frontend.Services.Video;

// encoded video coming in, one H.264 access unit at a time. where it comes from is the only thing that differs
// between your own stream (a channel fed by the publisher) and someone else's (a shared memory ring)
public interface IEncodedVideoSource
{
    // waits up to timeout. false if nothing came (no stream yet, or none in time)
    bool TryReadAccessUnit(TimeSpan timeout, out ReadOnlyMemory<byte> accessUnit);
}
