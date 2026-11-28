using NAudio.Wave;

namespace SGVS.Frontend.Services.Audio;

public sealed class PcmPlayer : IPcmSink, IDisposable
{
    // IPcmSink chunks are 16-bit
    private const int BitsPerSample = 16;

    // how much decoded audio may wait to play. past this, new samples are dropped (DiscardOnBufferOverflow)
    private static readonly TimeSpan BufferLength = TimeSpan.FromMilliseconds(250);

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
            rate: OpusPacketDecoder.SampleRate,
            bits: BitsPerSample,
            channels: OpusPacketDecoder.Channels);

        _buffer = new BufferedWaveProvider(format, BufferLength);
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