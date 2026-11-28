using SGVS.Frontend.Services.Video;
using Xunit;

namespace SGVS.Frontend.Tests.Video;

public class FfmpegTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sgvs-ffmpeg").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Func<string, string?> Env(string? ffmpeg, string? path) =>
        name => name switch { "SGVS_FFMPEG" => ffmpeg, "PATH" => path, _ => null };

    [Fact]
    public void Find_prefers_SGVS_FFMPEG_when_the_file_exists()
    {
        var fake = Path.Combine(_dir, "my-ffmpeg");
        File.WriteAllText(fake, "");

        Assert.Equal(fake, Ffmpeg.Find(Env(fake, _dir)));
    }

    [Fact]
    public void Find_ignores_SGVS_FFMPEG_pointing_at_nothing()
    {
        Assert.Null(Ffmpeg.Find(Env(Path.Combine(_dir, "nope"), _dir)));
    }

    [Fact]
    public void Find_looks_through_PATH()
    {
        var exe = Path.Combine(_dir, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        File.WriteAllText(exe, "");

        Assert.Equal(exe, Ffmpeg.Find(Env(null, _dir)));
    }
}
