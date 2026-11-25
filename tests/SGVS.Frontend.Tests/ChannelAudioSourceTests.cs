using System.Threading.Channels;
using SGVS.Frontend.Services.Audio;
using SGVS.Frontend.Tests.Support;
using SharedMemLibrary;
using Xunit;

namespace SGVS.Frontend.Tests;

public class ChannelAudioSourceTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(1);

    private readonly Channel<GCFrame> _queue = Channel.CreateUnbounded<GCFrame>();
    private readonly List<TimeSpan> _sleeps = [];
    private readonly ChannelAudioSource _source;

    public ChannelAudioSourceTests() => _source = new ChannelAudioSource(_queue.Reader, _sleeps.Add);

    private void Write(byte[] packet) => Assert.True(_queue.Writer.TryWrite(TestFrames.Packet(packet)));

    // the first read empties the queue: do it while the queue is empty, so later writes are returned
    private void FirstRead() => Assert.False(_source.TryReadPacket(Short, out _));

    [Fact]
    public void Returns_the_packet_cut_to_its_frame_size()
    {
        FirstRead();
        var opus = Enumerable.Range(1, 80).Select(i => (byte)i).ToArray();
        Write(opus);

        Assert.True(_source.TryReadPacket(Long, out var packet));
        Assert.Equal(opus, packet.ToArray());
    }

    [Fact]
    public void Drops_frames_queued_before_the_first_read()
    {
        Write([1]);
        Write([2]);
        Write([3]);

        Assert.False(_source.TryReadPacket(Short, out _));

        Write([4]);
        Assert.True(_source.TryReadPacket(Long, out var packet));
        Assert.Equal([4], packet.ToArray());
    }

    [Fact]
    public void With_an_empty_queue_it_returns_false_after_the_timeout()
    {
        FirstRead();

        Assert.False(_source.TryReadPacket(Short, out var packet));
        Assert.True(packet.IsEmpty);
        Assert.Empty(_sleeps); // it waited on the channel, not in sleep
    }

    [Fact]
    public async Task Returns_a_frame_written_while_it_waits()
    {
        var token = TestContext.Current.CancellationToken;
        FirstRead();

        var writer = Task.Run(async () =>
        {
            await Task.Delay(50, token);
            Write([7, 8, 9]);
        }, token);

        Assert.True(_source.TryReadPacket(Long, out var packet));
        Assert.Equal([7, 8, 9], packet.ToArray());
        await writer;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1276)]
    public void Rejects_a_frame_whose_size_does_not_fit_its_buffer(int size)
    {
        FirstRead();
        Assert.True(_queue.Writer.TryWrite(TestFrames.Frame(new byte[TestFrames.SlotSize], size)));

        Assert.False(_source.TryReadPacket(Long, out var packet));
        Assert.True(packet.IsEmpty);
    }

    [Fact]
    public void A_closed_queue_waits_out_the_timeout_instead_of_spinning()
    {
        var timeout = TimeSpan.FromMilliseconds(100);
        _queue.Writer.Complete();

        Assert.False(_source.TryReadPacket(timeout, out _));

        var slept = Assert.Single(_sleeps);
        Assert.InRange(slept, timeout - TimeSpan.FromMilliseconds(50), timeout);
    }
}
