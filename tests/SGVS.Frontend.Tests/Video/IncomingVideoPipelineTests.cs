using System.Diagnostics;
using System.Threading.Channels;
using SGVS.Frontend.Services.Video;
using SGVS.Frontend.Tests.Support;
using Xunit;

namespace SGVS.Frontend.Tests.Video;

public class IncomingVideoPipelineTests
{
    private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();
    private readonly FakeVideoDecoder _decoder = new();
    private readonly RecordingSink _sink = new();

    private IncomingVideoPipeline Create() => new(new ChannelVideoSource(_channel.Reader), _decoder, _sink);

    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline) Thread.Sleep(10);
    }

    [Fact]
    public void Feeds_access_units_from_source_to_decoder()
    {
        var pipeline = Create();
        pipeline.Start();

        _channel.Writer.TryWrite([1]);
        _channel.Writer.TryWrite([2]);
        _channel.Writer.TryWrite([3]);
        WaitUntil(() => _decoder.Received.Length == 3);
        pipeline.Stop();

        Assert.Equal([[1], [2], [3]], _decoder.Received);
        Assert.Equal(3, pipeline.AccessUnits);
    }

    // ffmpeg that stops reading its input leaves Decode stuck writing. Stop must still return (it is called on
    // the UI thread when you leave or stop watching): disposing the decoder is what unblocks the write
    [Fact]
    public void Stop_returns_even_while_the_decoder_is_stuck()
    {
        var stuck = new StuckDecoder();
        var pipeline = new IncomingVideoPipeline(new ChannelVideoSource(_channel.Reader), stuck, _sink);
        pipeline.Start();
        _channel.Writer.TryWrite([1]);
        Assert.True(stuck.Entered.Wait(TimeSpan.FromSeconds(2)));

        var stop = Task.Run(pipeline.Stop);

        Assert.True(stop.Wait(TimeSpan.FromSeconds(3)), "Stop waited on a decoder that never returns");
        Assert.True(_sink.Disposed);
    }

    private sealed class StuckDecoder : IVideoDecoder
    {
        private readonly ManualResetEventSlim _released = new();
        public ManualResetEventSlim Entered { get; } = new();

        public event Action<byte[]>? FrameDecoded { add { } remove { } }
        public event Action? Failed { add { } remove { } }

        public void Decode(ReadOnlySpan<byte> accessUnit)
        {
            Entered.Set();
            _released.Wait(); // until Dispose, like a write to a pipe nobody reads
        }

        public void Dispose() => _released.Set();
    }

    [Fact]
    public void Decoded_frames_reach_the_sink()
    {
        var pipeline = Create();
        pipeline.Start();

        _decoder.Raise([9, 9, 9]);
        pipeline.Stop();

        Assert.Equal([[9, 9, 9]], _sink.Frames);
    }

    [Fact]
    public void Stop_returns_and_disposes_decoder_and_sink()
    {
        var pipeline = Create();
        pipeline.Start();
        var clock = Stopwatch.StartNew();

        pipeline.Stop();
        pipeline.Stop(); // twice is fine

        Assert.True(clock.ElapsedMilliseconds < 500, $"Stop took {clock.ElapsedMilliseconds} ms");
        Assert.True(_decoder.Disposed);
        Assert.True(_sink.Disposed);
    }

    private sealed class FailingSource : IEncodedVideoSource
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public bool TryReadAccessUnit(TimeSpan timeout, out ReadOnlyMemory<byte> accessUnit)
        {
            Interlocked.Increment(ref _calls);
            accessUnit = default;
            return false;
        }
    }

    [Fact]
    public void A_source_that_fails_at_once_is_polled_every_few_milliseconds()
    {
        var source = new FailingSource();
        var pipeline = new IncomingVideoPipeline(source, _decoder, _sink);
        pipeline.Start();

        Thread.Sleep(300);
        pipeline.Stop();

        // waiting out the whole 100 ms read would allow only about 3 polls, and frames would arrive in bursts
        Assert.True(source.Calls >= 15, $"only {source.Calls} polls in 300 ms");
    }

    [Fact]
    public void Decoder_exception_is_counted_not_thrown()
    {
        _decoder.ThrowOnDecode = true;
        var pipeline = Create();
        pipeline.Start();

        _channel.Writer.TryWrite([1]);
        WaitUntil(() => pipeline.Errors >= 1);
        _channel.Writer.TryWrite([2]);
        WaitUntil(() => pipeline.Errors >= 2); // the loop is still alive after the first failure
        pipeline.Stop();

        Assert.True(pipeline.Errors >= 2);
    }
}
