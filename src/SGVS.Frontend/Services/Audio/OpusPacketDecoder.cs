using System.Runtime.InteropServices;
using Concentus.Structs;

namespace SGVS.Frontend.Services.Audio;

// Decodes the incoming call audio: 48 kHz stereo Opus, one 10 ms packet at a time.
// Not thread-safe: Opus decoding carries state from one packet to the next, so use one instance per stream.
public sealed class OpusPacketDecoder
{
    public const int SampleRate = 48_000, Channels = 2, SamplesPerChannel = 480;
    public const int PcmBytes = SamplesPerChannel * Channels * sizeof(short); // 1920

    private const int MaxPacketBytes = 1275; // largest Opus packet (RFC 6716)

    // The managed decoder on purpose: OpusCodecFactory.CreateDecoder loads a native libopus when it finds
    // one, so behaviour would depend on the machine. The constructor is obsolete only to push people to the factory.
#pragma warning disable CS0618
    private readonly OpusDecoder _decoder = new(SampleRate, Channels);
#pragma warning restore CS0618

    // Decodes one 10 ms packet into pcm as 16-bit little-endian samples, interleaved L R.
    // False if the packet is empty, too large, not exactly 10 ms, or doesn't decode. Never throws for a bad packet.
    public bool TryDecode(ReadOnlySpan<byte> packet, Span<byte> pcm)
    {
        if (pcm.Length < PcmBytes)
            throw new ArgumentException($"needs room for {PcmBytes} bytes", nameof(pcm));

        // Concentus treats an empty packet as packet loss and invents audio; that's not wanted here
        if (packet.IsEmpty || packet.Length > MaxPacketBytes)
            return false;

        var samples = MemoryMarshal.Cast<byte, short>(pcm[..PcmBytes]);
        try
        {
            // a longer packet doesn't fit 480 samples per channel, so Concentus rejects it
            return _decoder.Decode(packet, samples, SamplesPerChannel) == SamplesPerChannel;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
