using NAudio.Wave;

namespace SGVS.Frontend.ViewModels;

public sealed class PcmPlayer
{
    private readonly WaveFormat _format = new(44100, 16, 2);

    public async Task PlayAsync(string filePath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "PCM playback requires Windows.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException(
                $"PCM file was not found: {filePath}",
                filePath);

        var pcm = await File.ReadAllBytesAsync(filePath);

        using var memory = new MemoryStream(pcm, writable: false);
        using var source = new RawSourceWaveStream(memory, _format);
        using var output = new WaveOut();

        var stopped = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        output.PlaybackStopped += (_, _) => stopped.TrySetResult();

        output.Init(source);
        output.Play();

        await stopped.Task;
    }
}