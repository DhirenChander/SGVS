using SGVS.Frontend.Services.SharedMemory;
using SharedMemLibrary;
using Xunit;

namespace SGVS.Frontend.Tests;

public class SharedMemoryBridgeTests
{
    // just enough of the frame buffer to see what the bridge asks of it
    private sealed class RecordingFrameBuffer : IFrameBuffer, IDisposable
    {
        public bool Sharing;
        public bool Disposed;
        public void Dispose() => Disposed = true;
        public void SetShareVideo(bool sharing) => Sharing = sharing;
        public bool GetSharingVideo() => Sharing;

        public bool PushFrames(Frame[] f) => throw new NotSupportedException();
        public (GCFrame[] frames, bool success) TryPopFrames(Encoding encoding, uint max_frame_count) => throw new NotSupportedException();
        public (GCFrame[] frames, bool success) BlockingPopFrames(Encoding encoding, uint max_frame_count, uint max_timeout) => throw new NotSupportedException();
        public bool RequestEncoding(Encoding encoding) => throw new NotSupportedException();
        public bool RemoveEncoding(Encoding encoding) => throw new NotSupportedException();
        public (Encoding[] encodings, bool success) BlockingReadEncodings(uint max_len, uint max_timeout) => throw new NotSupportedException();
        public bool AlignRingsToConfig() => throw new NotSupportedException();
        public (Encoding[] encodings, bool success) ReadEncodings(uint max_len) => throw new NotSupportedException();
        public void SetFrontEndEncoding(Encoding encoding) => throw new NotSupportedException();
        public Encoding GetFrontEndEncoding() => throw new NotSupportedException();
    }

    [Fact]
    public void Bridge_writes_the_sharing_flag_to_the_frame_buffer()
    {
        var buffer = new RecordingFrameBuffer();
        var bridge = new SharedMemoryBridge(buffer);

        bridge.SetSharingVideo(true);
        Assert.True(buffer.Sharing);
        Assert.True(bridge.IsSharingVideo);

        bridge.SetSharingVideo(false);
        Assert.False(buffer.Sharing);
    }

    [Fact]
    public void Disposing_the_bridge_disposes_the_frame_buffer()
    {
        var buffer = new RecordingFrameBuffer();

        new SharedMemoryBridge(buffer).Dispose();

        Assert.True(buffer.Disposed);
    }

    [Fact]
    public void The_stand_in_remembers_the_flag()
    {
        using var bridge = new NullSharedMemoryBridge();

        Assert.False(bridge.IsSharingVideo);
        bridge.SetSharingVideo(true);
        Assert.True(bridge.IsSharingVideo);
    }

    [Fact]
    public void Create_picks_the_right_bridge_for_the_platform()
    {
        if (!OperatingSystem.IsWindows())
            Assert.IsType<NullSharedMemoryBridge>(SharedMemoryBridge.Create());
        // on Windows Create opens the real shared memory, covered by the native tests below
    }

    // ---- the real shared memory library, Windows only ----

    [Fact]
    public void Native_sharing_flag_is_visible_through_a_second_handle()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the shared memory library only exists on Windows");

        using var frontend = new SharedMemoryBridge();
        using var processing = new SharedMemoryBridge(); // same named mapping, like another process would open

        frontend.SetSharingVideo(true);
        Assert.True(processing.IsSharingVideo);

        frontend.SetSharingVideo(false);
        Assert.False(processing.IsSharingVideo);
    }

    [Fact]
    public void Native_encodings_requested_by_one_handle_are_listed_by_another()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the shared memory library only exists on Windows");

        using var one = new GCFrameBuffer();
        using var two = new GCFrameBuffer();

        var encoding = SharedMemLib.NewAudioEncoding(true);
        SharedMemLib.SetEncodingOutgoing(ref encoding);
        Assert.True(one.RequestEncoding(encoding));

        try
        {
            var (encodings, ok) = two.ReadEncodings(10);

            Assert.True(ok);
            Assert.Contains(encodings, e => e.Equals(encoding));
            Assert.True(encoding.IsAudio());
            Assert.True(encoding.IsOutgoing());
        }
        finally
        {
            one.RemoveEncoding(encoding);
        }
    }
}
