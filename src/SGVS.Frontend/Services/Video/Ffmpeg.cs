using System.Diagnostics;

namespace SGVS.Frontend.Services.Video;

// finds ffmpeg and starts it with pipes. the stream is binary (H.264 out, BGRA out), so callers must use
// the BaseStream of StandardOutput / StandardInput, never the text readers
public static class Ffmpeg
{
    public const string NotFoundMessage =
        "FFmpeg was not found. Install it with 'winget install --id Gyan.FFmpeg -e' or set SGVS_FFMPEG to ffmpeg.exe.";

    // SGVS_FFMPEG wins when it points at a real file, then every PATH entry. null if neither has ffmpeg.
    // getEnv is only for tests, it defaults to the process environment
    public static string? Find(Func<string, string?>? getEnv = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;

        var explicitPath = getEnv("SGVS_FFMPEG");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            return explicitPath;

        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var path = getEnv("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim().Trim('"'), name);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    // args go through ArgumentList so paths with spaces survive. stderr lines go to the trace output
    public static Process Start(string ffmpegPath, IReadOnlyList<string> args, bool writesStdin)
    {
        var info = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = writesStdin,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);

        var process = new Process { StartInfo = info };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) Trace.WriteLine("[ffmpeg] " + e.Data);
        };
        process.Start();
        // an undrained stderr pipe blocks ffmpeg once it fills
        process.BeginErrorReadLine();
        return process;
    }
}
