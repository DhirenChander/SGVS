using NAudio.Wave;
using SGVS.Frontend.Services.Audio;

namespace SGVS.Frontend.ViewModels;

public sealed class PcmPlayer : IPcmSink, IDisposable
{
    private readonly BufferedWaveProvider _buffer;
    private readonly WaveOut _output;
    private readonly object _disposeLock = new();
    private bool _disposed;

    public PcmPlayer()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "PCM playback requires Windows.");

        var format = new WaveFormat(
            rate: 48000,
            bits: 16,
            channels: 2);

        _buffer = new BufferedWaveProvider(format, TimeSpan.FromMilliseconds(250));
        _buffer.DiscardOnBufferOverflow = true;

        _output = new WaveOut();
        _output.Init(_buffer);
        _output.Play();
    }

    public void Enqueue(ReadOnlySpan<byte> pcm)
    {
        lock (_disposeLock)
        {
            if (_disposed)
                return;

            if (pcm.Length == 0)
                return;

            _buffer.AddSamples(pcm);
        }
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed)
                return;

            _disposed = true;
            if (OperatingSystem.IsWindows())
            {
                _output.Stop();
                _output.Dispose();
            }

        }
    }
}