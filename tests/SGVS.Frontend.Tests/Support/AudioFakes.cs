using Concentus.Enums;
using Concentus.Structs;

namespace SGVS.Frontend.Tests.Support;

// Opus packets made the way the processing module makes them (#13): 48 kHz, stereo, 10 ms, CBR, 64 kbps.
public static class OpusTestPackets
{
    public const int SampleRate = 48_000, Channels = 2;

    // Encodes consecutive frames of a 440 Hz sine on the left channel only; the right channel is silent.
    // One encoder for all frames, so later packets carry on from earlier ones like a real stream.
    public static List<byte[]> LeftSine(int count, int samplesPerChannel = 480)
    {
#pragma warning disable CS0618 // managed encoder on purpose: the factory may load a native libopus
        var encoder = new OpusEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_AUDIO)
        {
            Bitrate = 64_000,
            UseVBR = false,
        };
#pragma warning restore CS0618

        var packets = new List<byte[]>();
        var pcm = new short[samplesPerChannel * Channels];
        var buffer = new byte[1275];
        for (int p = 0; p < count; p++)
        {
            for (int i = 0; i < samplesPerChannel; i++)
            {
                int n = p * samplesPerChannel + i;
                pcm[2 * i] = (short)(8000 * Math.Sin(n * 2 * Math.PI * 440 / SampleRate));
                pcm[2 * i + 1] = 0;
            }
            int length = encoder.Encode(pcm, samplesPerChannel, buffer, buffer.Length);
            packets.Add(buffer[..length]);
        }
        return packets;
    }
}
