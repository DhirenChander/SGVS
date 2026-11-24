using SGVS.Frontend.Services.Video;
using Xunit;

namespace SGVS.Frontend.Tests.Video;

public class FfmpegStreamPublisherTests
{
    private static string RequireFfmpeg()
    {
        var path = Ffmpeg.Find();
        Assert.SkipWhen(path is null, "ffmpeg is not installed");
        return path!;
    }

    // a generated picture instead of the dummy mp4, so the tests need no download
    private static readonly string[] OneSecondOfTestPattern = ["-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30", "-t", "1"];

    private static List<int> NalTypes(byte[] au)
    {
        var types = new List<int>();
        for (var i = 0; i + 3 < au.Length; i++)
            if (au[i] == 0 && au[i + 1] == 0 && au[i + 2] == 1)
                types.Add(au[i + 3] & 0x1F);
        return types;
    }

    [Fact]
    public void Encodes_one_access_unit_per_frame()
    {
        using var publisher = new FfmpegStreamPublisher(RequireFfmpeg(), OneSecondOfTestPattern);
        var units = new List<byte[]>();
        var failures = new List<string>();
        using var ended = new ManualResetEventSlim();
        publisher.AccessUnitEncoded += au => { lock (units) units.Add(au); };
        publisher.Failed += message => { failures.Add(message); ended.Set(); };

        publisher.Start();

        Assert.True(ended.Wait(TimeSpan.FromSeconds(10)), "the finite input should end the encoder");
        Assert.Single(failures); // a finite input ending is reported once, as the encoder stopping
        Assert.Equal(30, units.Count);
        Assert.All(units, au => Assert.Equal<byte>([0, 0, 0, 1, 9], au[..5]));
        var first = NalTypes(units[0]);
        Assert.Contains(7, first); // SPS, so a late joiner can decode from the first keyframe
        Assert.Contains(5, first); // IDR slice
    }

    [Fact]
    public void Stop_kills_ffmpeg()
    {
        using var publisher = new FfmpegStreamPublisher(RequireFfmpeg(),
            ["-re", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30"]);
        using var first = new ManualResetEventSlim();
        var failed = false;
        publisher.AccessUnitEncoded += _ => first.Set();
        publisher.Failed += _ => failed = true;
        publisher.Start();
        Assert.True(first.Wait(TimeSpan.FromSeconds(10)));

        publisher.Stop();

        Assert.False(publisher.IsRunning);
        Assert.False(failed);
    }

    [Fact]
    public void Create_dummy_without_the_video_explains_how_to_get_it()
    {
        RequireFfmpeg();
        Assert.SkipWhen(File.Exists(Path.Combine(AppContext.BaseDirectory, "StreamVideo", "rocket-league.mp4")),
            "the dummy video is present");

        var error = Assert.Throws<InvalidOperationException>(FfmpegStreamPublisher.CreateDummy);

        Assert.Contains("get-dummy-video.ps1", error.Message);
    }
}
