using SharedMemLibrary;
using Xunit;

namespace SGVS.Frontend.Tests;

// real native shared memory, so Windows only
public class SharedMemoryPushBytesTests
{
    private static GCFrameBuffer Open()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "needs the native shared memory library");
        return new GCFrameBuffer();
    }

    private static Encoding NewVideoRing(GCFrameBuffer fb)
    {
        var enc = SharedMemLib.NewVideoEncoding(3000, 3000, 720, 1280, 30);
        SharedMemLib.SetEncodingIncoming(ref enc);
        Assert.True(fb.RequestEncoding(enc));
        return enc;
    }

    [Fact]
    public void Push_bytes_round_trips_through_a_real_video_ring()
    {
        using var fb = Open();
        var enc = NewVideoRing(fb);
        var data = Enumerable.Repeat((byte)0x5A, 10_000).ToArray();

        Assert.True(fb.PushBytes(enc, data));

        var (frames, ok) = fb.BlockingPopFrames(enc, 1, 1000);
        Assert.True(ok);
        var frame = Assert.Single(frames);
        Assert.Equal((nuint)10_000, frame.raw.size);
        Assert.All(frame.Data.Take(10_000), b => Assert.Equal(0x5A, b));
    }

    [Fact]
    public void Push_bytes_rejects_a_frame_over_500000_bytes()
    {
        using var fb = Open();
        var enc = NewVideoRing(fb);

        Assert.False(fb.PushBytes(enc, new byte[GCFrameBuffer.MaxVideoFrameBytes + 1]));
    }
}
