using System.Diagnostics;
using SGVS.Frontend.Services.Audio;
using SGVS.Frontend.Tests.Support;
using Xunit;

namespace SGVS.Frontend.Tests;

public class IncomingAudioPumpTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    private readonly FakeAudioSource _source = new();
    private readonly FakePcmSink _sink = new();
    private readonly List<TimeSpan> _sleeps = [];
    private readonly IncomingAudioPump _pump;

    public IncomingAudioPumpTests() => _pump = new IncomingAudioPump(_source, _sink, _sleeps.Add);

    // what a fresh decoder makes of the same packets, to check the pump's output and its order
    private static List<byte[]> Decode(IEnumerable<byte[]> packets)
    {
        var decoder = new OpusPacketDecoder();
        return packets.Select(p =>
        {
            var pcm = new byte[OpusPacketDecoder.PcmBytes];
            Assert.True(decoder.TryDecode(p, pcm));
            return pcm;
        }).ToList();
    }

    [Fact]
    public void Decoded_packets_reach_the_sink_in_order()
    {
        var packets = OpusTestPackets.LeftSine(3);
        packets.ForEach(_source.Push);

        for (int i = 0; i < 3; i++)
            Assert.True(_pump.PumpOnce(Timeout));

        Assert.Equal(Decode(packets), _sink.Chunks);
        Assert.All(_sink.Chunks, chunk => Assert.Equal(1920, chunk.Length));
        Assert.Equal(3, _pump.PacketsDecoded);
    }

    [Fact]
    public void A_packet_that_does_not_decode_is_dropped()
    {
        _source.Push([0xF3, 0x00]);
        _source.Push(OpusTestPackets.LeftSine(1)[0]);

        _pump.PumpOnce(Timeout);
        _pump.PumpOnce(Timeout);

        Assert.Single(_sink.Chunks);
        Assert.Equal(1, _pump.PacketsDropped);
        Assert.Equal(1, _pump.PacketsDecoded);
    }

    [Fact]
    public void Without_a_packet_nothing_reaches_the_sink()
    {
        Assert.False(_pump.PumpOnce(TimeSpan.FromMilliseconds(1)));
        Assert.Empty(_sink.Chunks);
    }

    [Fact]
    public void A_sink_that_throws_once_does_not_stop_the_audio()
    {
        var packets = OpusTestPackets.LeftSine(2);
        packets.ForEach(_source.Push);
        _sink.ThrowOnce = true;

        _pump.PumpOnce(Timeout);
        _pump.PumpOnce(Timeout);

        Assert.Single(_sink.Chunks);
        Assert.Equal(1, _pump.Errors);
        Assert.IsType<InvalidOperationException>(_pump.LastError);
    }

    [Fact]
    public void A_source_that_throws_is_recorded_and_waited_out()
    {
        var error = new ObjectDisposedException("frame buffer");
        _source.ThrowNext = error;

        Assert.False(_pump.PumpOnce(Timeout));

        Assert.Same(error, _pump.LastError);
        Assert.Equal(1, _pump.Errors);
        Assert.Equal([Timeout], _sleeps);
    }

    [Fact]
    public void On_its_own_thread_it_plays_queued_packets_and_stops_promptly()
    {
        var pump = new IncomingAudioPump(_source, _sink);
        OpusTestPackets.LeftSine(3).ForEach(_source.Push);

        pump.Start();
        pump.Start(); // a second call does nothing
        Assert.True(_sink.WaitForChunks(3, TimeSpan.FromSeconds(5)), "packets should reach the sink");

        var clock = Stopwatch.StartNew();
        pump.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"Stop took {clock.Elapsed}");

        pump.Dispose(); // safe after Stop
        Assert.Equal(3, _sink.Chunks.Count);
    }
}
