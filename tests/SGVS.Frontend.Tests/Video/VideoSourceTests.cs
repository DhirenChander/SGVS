using System.Diagnostics;
using System.Threading.Channels;
using SGVS.Frontend.Services.Video;
using SGVS.Frontend.Tests.Support;
using Xunit;

namespace SGVS.Frontend.Tests.Video;

public class VideoSourceTests
{
    [Fact]
    public void Channel_source_returns_written_access_units()
    {
        var channel = Channel.CreateUnbounded<byte[]>();
        channel.Writer.TryWrite([1, 2, 3]);
        var source = new ChannelVideoSource(channel.Reader);

        Assert.True(source.TryReadAccessUnit(TimeSpan.FromMilliseconds(50), out var au));

        Assert.Equal([1, 2, 3], au.ToArray());
    }

    [Fact]
    public void Channel_source_times_out_when_empty()
    {
        var source = new ChannelVideoSource(Channel.CreateUnbounded<byte[]>().Reader);
        var clock = Stopwatch.StartNew();

        Assert.False(source.TryReadAccessUnit(TimeSpan.FromMilliseconds(50), out _));

        Assert.True(clock.ElapsedMilliseconds >= 40, $"returned after {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Channel_source_returns_false_once_completed()
    {
        var channel = Channel.CreateUnbounded<byte[]>();
        channel.Writer.Complete();
        var source = new ChannelVideoSource(channel.Reader);
        var clock = Stopwatch.StartNew();

        Assert.False(source.TryReadAccessUnit(TimeSpan.FromSeconds(5), out _));

        Assert.True(clock.ElapsedMilliseconds < 1000);
    }

    [Fact]
    public void Shared_memory_source_reads_one_access_unit_per_frame_from_its_encoding()
    {
        var buffer = new FakeFrameBuffer();
        var enc = VideoEncodings.NewIncoming();
        buffer.PushVideo([1, 1]);
        buffer.PushVideo([2, 2, 2]);
        var source = new SharedMemoryVideoSource(buffer, enc);

        Assert.True(source.TryReadAccessUnit(TimeSpan.FromMilliseconds(50), out var first));
        Assert.True(source.TryReadAccessUnit(TimeSpan.FromMilliseconds(50), out var second));

        Assert.Equal([1, 1], first.ToArray());
        Assert.Equal([2, 2, 2], second.ToArray());
        Assert.Equal(2, buffer.PoppedWith.Count);
        Assert.All(buffer.PoppedWith, e => Assert.Equal(enc.id[0], e.id[0]));
        Assert.Equal(1, buffer.AlignCalls);
    }

    [Fact]
    public void Shared_memory_source_retries_soon_after_a_failed_pop()
    {
        var slept = new List<TimeSpan>();
        var source = new SharedMemoryVideoSource(new FakeFrameBuffer(), VideoEncodings.NewIncoming(), slept.Add);

        Assert.False(source.TryReadAccessUnit(TimeSpan.FromMilliseconds(100), out _));

        // a failed pop can leave the ring's wake-up event set. sleeping out the whole timeout would let several
        // frames pile up and play as a burst, so it only backs off a few milliseconds
        Assert.Single(slept);
        Assert.InRange(slept[0], TimeSpan.FromTicks(1), TimeSpan.FromMilliseconds(10));
    }
}
