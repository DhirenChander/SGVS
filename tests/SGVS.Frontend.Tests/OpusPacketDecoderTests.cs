using System.Buffers.Binary;
using SGVS.Frontend.Services.Audio;
using SGVS.Frontend.Tests.Support;
using Xunit;

namespace SGVS.Frontend.Tests;

public class OpusPacketDecoderTests
{
    private readonly OpusPacketDecoder _decoder = new();

    [Fact]
    public void A_10_ms_packet_decodes_to_one_1920_byte_chunk()
    {
        var packet = OpusTestPackets.LeftSine(1)[0];
        var pcm = new byte[OpusPacketDecoder.PcmBytes];

        Assert.True(_decoder.TryDecode(packet, pcm));
        Assert.Equal(1920, OpusPacketDecoder.PcmBytes);
    }

    [Fact]
    public void Left_and_right_channels_stay_apart()
    {
        // the encoder's 312-sample look-ahead makes the first packet quieter, so check a later one
        var packets = OpusTestPackets.LeftSine(5);
        var pcm = new byte[OpusPacketDecoder.PcmBytes];
        foreach (var packet in packets)
            Assert.True(_decoder.TryDecode(packet, pcm));

        var (left, right) = ChannelRms(pcm);
        Assert.True(left > 3000, $"left RMS {left:F0} should be loud");
        Assert.True(right < 300, $"right RMS {right:F0} should be near silent");
    }

    [Fact]
    public void An_empty_packet_is_rejected_and_nothing_is_written()
    {
        var pcm = Enumerable.Repeat((byte)0xAB, OpusPacketDecoder.PcmBytes).ToArray();

        Assert.False(_decoder.TryDecode(ReadOnlySpan<byte>.Empty, pcm));
        Assert.All(pcm, b => Assert.Equal(0xAB, b));
    }

    [Fact]
    public void A_corrupt_packet_is_rejected_without_throwing()
    {
        // a 10 ms code-3 packet that claims zero frames: Concentus reports a corrupted stream
        byte[] corrupt = [0xF3, 0x00];
        var pcm = new byte[OpusPacketDecoder.PcmBytes];

        Assert.False(_decoder.TryDecode(corrupt, pcm));
    }

    [Fact]
    public void A_20_ms_packet_is_rejected()
    {
        var packet = OpusTestPackets.LeftSine(1, samplesPerChannel: 960)[0];
        var pcm = new byte[OpusPacketDecoder.PcmBytes];

        Assert.False(_decoder.TryDecode(packet, pcm));
    }

    // even sample indexes are left, odd are right; samples are little-endian 16-bit
    private static (double Left, double Right) ChannelRms(byte[] pcm)
    {
        double left = 0, right = 0;
        int samples = pcm.Length / 4;
        for (int i = 0; i < samples; i++)
        {
            double l = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(4 * i));
            double r = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(4 * i + 2));
            left += l * l;
            right += r * r;
        }
        return (Math.Sqrt(left / samples), Math.Sqrt(right / samples));
    }
}
