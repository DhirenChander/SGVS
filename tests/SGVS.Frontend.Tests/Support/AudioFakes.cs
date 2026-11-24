using System.Collections.Concurrent;
using Concentus.Enums;
using Concentus.Structs;
using SGVS.Frontend.Services.Audio;
using SharedMemLibrary;
using Encoding = SharedMemLibrary.Encoding;

namespace SGVS.Frontend.Tests.Support;

// Packets queued by the test. Safe to fill from the test while the pump reads on its own thread.
public sealed class FakeAudioSource : IIncomingAudioSource
{
    private readonly BlockingCollection<byte[]> _packets = new();

    public Exception? ThrowNext { get; set; }

    public void Push(byte[] packet) => _packets.Add(packet);

    public bool TryReadPacket(TimeSpan timeout, out ReadOnlyMemory<byte> packet)
    {
        if (ThrowNext is { } error)
        {
            ThrowNext = null;
            throw error;
        }
        bool got = _packets.TryTake(out var bytes, timeout);
        packet = got ? bytes : default;
        return got;
    }
}

// Copies every chunk it gets, like the real player must.
public sealed class FakePcmSink : IPcmSink
{
    private readonly List<byte[]> _chunks = [];

    public bool ThrowOnce { get; set; }

    public List<byte[]> Chunks
    {
        get { lock (_chunks) return [.. _chunks]; }
    }

    public void Enqueue(ReadOnlySpan<byte> pcm)
    {
        if (ThrowOnce)
        {
            ThrowOnce = false;
            throw new InvalidOperationException("player failed");
        }
        lock (_chunks) _chunks.Add(pcm.ToArray());
    }

    public bool WaitForChunks(int count, TimeSpan timeout) =>
        SpinWait.SpinUntil(() => { lock (_chunks) return _chunks.Count >= count; }, timeout);
}

// Frames shaped the way the wrapper hands them out: a 1275-byte buffer, and raw.size set to the real length.
// Built by hand, because the GCFrame(Encoding) constructor calls native code.
public static class TestFrames
{
    public const int SlotSize = 1275; // the wrapper always hands out 1275-byte buffers for audio

    public static GCFrame Frame(byte[] data, int size) =>
        new() { raw = new Frame { size = (nuint)size }, Data = data };

    public static GCFrame Packet(byte[] packet)
    {
        var data = new byte[SlotSize];
        packet.CopyTo(data, 0);
        return Frame(data, packet.Length);
    }
}

// Shared memory without the native library: a config of encodings and one queue of frames.
// Only the members SharedMemoryAudioSource uses work; the rest throw.
public sealed class FakeFrameBuffer : IFrameBuffer
{
    public List<Encoding> Encodings { get; } = [];
    public Queue<GCFrame> Frames { get; } = new();

    public int AlignCalls { get; private set; }
    public List<Encoding> PoppedWith { get; } = [];      // encoding passed to each BlockingPopFrames
    public List<Encoding> DrainedWith { get; } = [];     // encoding passed to each TryPopFrames
    public List<uint> DrainCounts { get; } = [];         // max_frame_count passed to each TryPopFrames
    public bool FailNextPop { get; set; }
    public List<Message> Messages { get; } = [];         // every PushMessage, in order
    public List<Encoding> Requested { get; } = [];       // every RequestEncoding, in order
    public List<(Encoding Enc, byte[] Data)> Pushed { get; } = []; // every successful PushBytes
    public bool FullRings { get; set; }                  // makes PushBytes fail, like a full ring

    // An encoding the tests can tell apart by tag. Never touches Encoding.id, which is about to change type.
    public static Encoding Enc(EncodingType type, EncodingDir dir, byte tag = 0)
    {
        var enc = new Encoding { type = type, dir = dir };
        enc.data.audio_enc.is_input = tag;
        return enc;
    }

    public void Push(byte[] packet) => Frames.Enqueue(TestFrames.Packet(packet));

    // a video access unit in a full-size video slot, like the real wrapper hands out
    public void PushVideo(byte[] accessUnit)
    {
        var data = new byte[GCFrameBuffer.MaxVideoFrameBytes];
        accessUnit.CopyTo(data, 0);
        Frames.Enqueue(TestFrames.Frame(data, accessUnit.Length));
    }

    public (Encoding[] encodings, bool success) ReadEncodings(uint max_len) =>
        ([.. Encodings.Take((int)max_len)], true);

    public bool AlignRingsToConfig()
    {
        AlignCalls++;
        return true;
    }

    public (GCFrame[] frames, bool success) TryPopFrames(Encoding encoding, uint max_frame_count)
    {
        DrainedWith.Add(encoding);
        DrainCounts.Add(max_frame_count);
        var frames = new List<GCFrame>();
        while (frames.Count < max_frame_count && Frames.TryDequeue(out var frame))
            frames.Add(frame);
        return ([.. frames], true);
    }

    // Never waits: an empty queue fails at once, like the real call does when it can't find the ring.
    public (GCFrame[] frames, bool success) BlockingPopFrames(Encoding encoding, uint max_frame_count, uint max_timeout)
    {
        PoppedWith.Add(encoding);
        if (FailNextPop)
        {
            FailNextPop = false;
            return ([], false);
        }
        return Frames.TryDequeue(out var frame) ? ([frame], true) : ([], false);
    }

    public bool PushFrames(Frame[] f) => throw new NotSupportedException();

    public bool PushBytes(Encoding encoding, ReadOnlySpan<byte> data)
    {
        var max = encoding.type == EncodingType.Video ? GCFrameBuffer.MaxVideoFrameBytes : GCFrameBuffer.MaxAudioFrameBytes;
        if (data.IsEmpty || data.Length > max || FullRings) return false;
        Pushed.Add((encoding, data.ToArray()));
        return true;
    }
    public bool RequestEncoding(Encoding encoding)
    {
        Requested.Add(encoding);
        return true;
    }

    public bool RemoveEncoding(Encoding encoding) => throw new NotSupportedException();
    public (Encoding[] encodings, bool success) BlockingReadEncodings(uint max_len, uint max_timeout) =>
        throw new NotSupportedException();
    public bool PushMessage(Message msg)
    {
        Messages.Add(msg);
        return true;
    }

    public (Message msg, bool success) BlockingPopMessage(Module src, Module dst, uint max_timeout) => (default, false);
}

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
