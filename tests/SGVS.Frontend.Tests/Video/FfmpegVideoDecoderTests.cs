using SGVS.Frontend.Services.Video;
using Xunit;

namespace SGVS.Frontend.Tests.Video;

public class FfmpegVideoDecoderTests
{
    private static string RequireFfmpeg()
    {
        var path = Ffmpeg.Find();
        Assert.SkipWhen(path is null, "ffmpeg is not installed");
        return path!;
    }

    // two seconds of test pattern encoded the way the app encodes: 60 access units, keyframes at 0 and 30
    private static readonly Lazy<List<byte[]>> TwoSeconds = new(() =>
    {
        // skips (rather than fails) every test that needs the clip when ffmpeg is not installed
        using var publisher = new FfmpegStreamPublisher(RequireFfmpeg(),
            ["-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30", "-t", "2"]);
        var units = new List<byte[]>();
        using var ended = new ManualResetEventSlim();
        publisher.AccessUnitEncoded += au => { lock (units) units.Add(au); };
        publisher.Failed += _ => ended.Set();
        publisher.Start();
        Assert.True(ended.Wait(TimeSpan.FromSeconds(20)));
        return units;
    });

    private sealed class FrameCounter
    {
        private int _count;
        private int _wrongSize;
        public int Count => Volatile.Read(ref _count);
        public int WrongSize => Volatile.Read(ref _wrongSize);

        public void Attach(IVideoDecoder decoder) => decoder.FrameDecoded += frame =>
        {
            if (frame.Length != VideoFormat.FrameBytes) Interlocked.Increment(ref _wrongSize);
            Interlocked.Increment(ref _count);
        };
    }

    [Fact]
    public void Decodes_every_frame_to_1280x720_bgra()
    {
        var units = TwoSeconds.Value;
        var frames = new FrameCounter();
        var decoder = new FfmpegVideoDecoder(RequireFfmpeg());
        frames.Attach(decoder);

        foreach (var au in units) decoder.Decode(au);
        decoder.Dispose(); // flushes what ffmpeg still holds

        Assert.Equal(60, frames.Count);
        Assert.Equal(0, frames.WrongSize);
    }

    [Fact]
    public void Decode_starting_mid_stream_still_produces_frames()
    {
        var units = TwoSeconds.Value;
        var frames = new FrameCounter();
        var decoder = new FfmpegVideoDecoder(RequireFfmpeg());
        frames.Attach(decoder);

        foreach (var au in units.Skip(5)) decoder.Decode(au);
        decoder.Dispose();

        // everything from the second keyframe (access unit 30) on must decode
        Assert.True(frames.Count >= 25, $"only {frames.Count} frames decoded");
    }

    [Fact]
    public void Decode_after_process_exit_does_not_throw()
    {
        var units = TwoSeconds.Value;
        var decoder = new FfmpegVideoDecoder(RequireFfmpeg());
        decoder.KillForTest();

        var error = Record.Exception(() =>
        {
            for (var i = 0; i < 10; i++) decoder.Decode(units[i]);
        });

        Assert.Null(error);
        decoder.Dispose();
    }

    [Fact]
    public void The_dummy_clip_plays_through_the_encoder_and_decoder()
    {
        RequireFfmpeg();
        Assert.SkipUnless(File.Exists(Path.Combine(AppContext.BaseDirectory, "StreamVideo", "rocket-league.mp4")),
            "the dummy video has not been downloaded (scripts/get-dummy-video.ps1)");
        using var publisher = FfmpegStreamPublisher.CreateDummy();
        var frames = new FrameCounter();
        using var decoder = new FfmpegVideoDecoder(Ffmpeg.Find()!);
        frames.Attach(decoder);
        publisher.AccessUnitEncoded += au => decoder.Decode(au);

        publisher.Start();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (frames.Count < 45 && DateTime.UtcNow < deadline) Thread.Sleep(20);

        Assert.True(frames.Count >= 45, $"only {frames.Count} frames came out of the dummy clip");
        Assert.Equal(0, frames.WrongSize);
    }

    [Fact]
    public void Frames_arrive_while_input_is_still_open()
    {
        var units = TwoSeconds.Value;
        var frames = new FrameCounter();
        using var decoder = new FfmpegVideoDecoder(RequireFfmpeg());
        frames.Attach(decoder);

        foreach (var au in units.Take(30))
        {
            decoder.Decode(au);
            Thread.Sleep(33);
        }

        // a decoder that only flushes on close would have produced nothing yet
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (frames.Count < 25 && DateTime.UtcNow < deadline) Thread.Sleep(20);
        Assert.True(frames.Count >= 25, $"only {frames.Count} frames while the input was open");
    }
}
