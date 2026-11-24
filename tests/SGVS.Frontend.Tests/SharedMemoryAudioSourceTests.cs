using SGVS.Frontend.Services.Audio;
using SGVS.Frontend.Tests.Support;
using SharedMemLibrary;
using Xunit;
using static SGVS.Frontend.Tests.Support.FakeFrameBuffer;
using static SGVS.Frontend.Tests.Support.TestFrames;

namespace SGVS.Frontend.Tests;

public class SharedMemoryAudioSourceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    private readonly FakeFrameBuffer _buffer = new();
    private readonly List<TimeSpan> _sleeps = [];
    private readonly SharedMemoryAudioSource _source;

    public SharedMemoryAudioSourceTests() => _source = new SharedMemoryAudioSource(_buffer, _sleeps.Add);

    private static byte Tag(SharedMemLibrary.Encoding enc) => enc.data.audio_enc.is_input;

    private bool Read(out ReadOnlyMemory<byte> packet) => _source.TryReadPacket(Timeout, out packet);

    // finds the encoding on the first read and drains the empty ring, so later pushes are returned
    private void FindIncomingAudio(byte tag = 1)
    {
        _buffer.Encodings.Add(Enc(EncodingType.Audio, EncodingDir.Incoming, tag));
        Read(out _);
        _sleeps.Clear();
    }

    [Fact]
    public void Without_incoming_audio_it_waits_out_the_timeout_and_never_pops()
    {
        _buffer.Encodings.Add(Enc(EncodingType.Audio, EncodingDir.Outgoing));
        _buffer.Encodings.Add(Enc(EncodingType.Video, EncodingDir.Incoming));

        Assert.False(Read(out _));
        Assert.Empty(_buffer.PoppedWith);
        Assert.Empty(_buffer.DrainedWith);
        Assert.Equal([Timeout], _sleeps);
    }

    [Fact]
    public void Finds_incoming_audio_aligns_once_and_pops_with_it()
    {
        _buffer.Encodings.Add(Enc(EncodingType.Audio, EncodingDir.Outgoing, tag: 9));
        FindIncomingAudio(tag: 1);
        _buffer.Push([1, 2, 3]);
        _buffer.Push([4, 5, 6]);

        Assert.True(Read(out _));
        Assert.True(Read(out _));

        Assert.Equal(1, _buffer.AlignCalls);
        Assert.All(_buffer.PoppedWith, enc => Assert.Equal(1, Tag(enc)));
    }

    [Fact]
    public void With_two_incoming_audio_encodings_it_uses_the_later_one()
    {
        _buffer.Encodings.Add(Enc(EncodingType.Audio, EncodingDir.Incoming, tag: 1));
        _buffer.Encodings.Add(Enc(EncodingType.Audio, EncodingDir.Incoming, tag: 2));
        _buffer.Push([1, 2, 3]);

        Read(out _);

        Assert.Equal(2, Tag(Assert.Single(_buffer.PoppedWith)));
    }

    [Fact(Skip = "the stale-packet drain in SharedMemoryAudioSource is commented out since the demo merge")]
    public void Drops_packets_already_queued_when_it_finds_the_encoding()
    {
        _buffer.Encodings.Add(Enc(EncodingType.Audio, EncodingDir.Incoming));
        _buffer.Push([1]);
        _buffer.Push([2]);
        _buffer.Push([3]);

        Assert.False(Read(out _));
        Assert.Equal([SharedMemoryAudioSource.RingCapacity], _buffer.DrainCounts);

        _buffer.Push([4]);
        Assert.True(Read(out var packet));
        Assert.Equal([4], packet.ToArray());
    }

    [Fact]
    public void Trims_the_packet_to_the_frame_size()
    {
        FindIncomingAudio();
        var opus = Enumerable.Range(1, 80).Select(i => (byte)i).ToArray();
        _buffer.Push(opus);

        Assert.True(Read(out var packet));
        Assert.Equal(opus, packet.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1276)]
    public void Rejects_a_frame_whose_size_does_not_fit_its_buffer(int size)
    {
        FindIncomingAudio();
        _buffer.Frames.Enqueue(Frame(new byte[1275], size));

        Assert.False(Read(out var packet));
        Assert.True(packet.IsEmpty);
    }

    [Fact]
    public void Finds_the_encoding_again_after_it_leaves_the_config()
    {
        FindIncomingAudio(tag: 1);
        _buffer.Encodings.Clear();
        _buffer.Encodings.Add(Enc(EncodingType.Audio, EncodingDir.Incoming, tag: 2));

        Assert.False(Read(out _)); // the pop fails and the old encoding is gone
        Assert.False(Read(out _)); // finds the new one, aligns and drains
        _buffer.Push([7]);
        Assert.True(Read(out var packet));

        Assert.Equal([7], packet.ToArray());
        Assert.Equal(2, _buffer.AlignCalls);
        Assert.Equal(2, Tag(_buffer.PoppedWith[^1]));
    }

    [Fact]
    public void Keeps_the_encoding_when_a_pop_fails_but_it_is_still_in_the_config()
    {
        FindIncomingAudio();
        _buffer.FailNextPop = true;

        Assert.False(Read(out _));
        _buffer.Push([7]);
        Assert.True(Read(out _));

        Assert.Equal(1, _buffer.AlignCalls);
    }

    [Fact]
    public void A_pop_that_fails_at_once_waits_out_the_rest_of_the_timeout()
    {
        FindIncomingAudio();
        _buffer.FailNextPop = true;

        Assert.False(Read(out _));

        var slept = Assert.Single(_sleeps);
        Assert.InRange(slept, Timeout - TimeSpan.FromMilliseconds(50), Timeout);
    }
}
