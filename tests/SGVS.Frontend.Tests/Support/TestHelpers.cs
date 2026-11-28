using SGVS.Frontend.Services.Video;
using Xunit;

namespace SGVS.Frontend.Tests.Support;

// polls instead of sleeping a fixed time. callers assert afterwards, so running out of time fails there
public static class Wait
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    public static void Until(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (!condition() && DateTime.UtcNow < deadline) Thread.Sleep(PollInterval);
    }
}

public static class TestFfmpeg
{
    // the ffmpeg to test against. skips the test when it is not installed
    public static string Require()
    {
        var path = Ffmpeg.Find();
        Assert.SkipWhen(path is null, "ffmpeg is not installed");
        return path!;
    }
}
