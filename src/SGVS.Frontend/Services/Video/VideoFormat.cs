namespace SGVS.Frontend.Services.Video;

// the one format the stream is encoded in and shown at. the encoder, the decoder, the shared memory encoding
// and the Go networking module (videoBitrateKbps in networking.go) all agree on these
public static class VideoFormat
{
    public const int Width = 1280;
    public const int Height = 720;
    public const int FrameRate = 30;
    public const int BitrateKbps = 3000;

    // one keyframe per second, so someone joining mid-stream waits at most that long for a picture
    public const int Gop = 30;

    // one decoded BGRA frame
    public const int FrameBytes = Width * Height * 4;

    // a remote stream with no frames for this long counts as stopped
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(2);
}
