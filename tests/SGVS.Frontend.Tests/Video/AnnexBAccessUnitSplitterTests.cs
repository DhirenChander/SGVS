using SGVS.Frontend.Services.Video;
using SharedMemLibrary;
using Xunit;

namespace SGVS.Frontend.Tests.Video;

public class AnnexBAccessUnitSplitterTests
{
    // delimiter (AUD NAL) then an IDR slice with n filler bytes
    private static byte[] Au(byte b, int n) =>
        [0, 0, 0, 1, 9, 0xF0, 0, 0, 0, 1, 0x65, .. Enumerable.Repeat(b, n)];

    [Fact]
    public void Splits_on_access_unit_delimiters()
    {
        var s = new AnnexBAccessUnitSplitter();

        var first = s.Push([.. Au(1, 50), .. Au(2, 60), .. Au(3, 70)]);

        Assert.Equal([Au(1, 50), Au(2, 60)], first);
        Assert.Equal(Au(3, 70), s.Flush());
    }

    [Fact]
    public void Handles_a_delimiter_split_across_chunks()
    {
        var s = new AnnexBAccessUnitSplitter();
        var all = new List<byte[]>();

        foreach (var b in (byte[])[.. Au(1, 50), .. Au(2, 60), .. Au(3, 70)])
            all.AddRange(s.Push([b]));
        if (s.Flush() is { } last) all.Add(last);

        Assert.Equal([Au(1, 50), Au(2, 60), Au(3, 70)], all);
    }

    [Fact]
    public void Drops_bytes_before_the_first_delimiter()
    {
        var s = new AnnexBAccessUnitSplitter();

        Assert.Empty(s.Push([0xAA, 0xBB, .. Au(1, 5)]));

        Assert.Equal(Au(1, 5), s.Flush());
    }

    [Fact]
    public void Discards_a_runaway_buffer_with_no_delimiter()
    {
        var s = new AnnexBAccessUnitSplitter();

        Assert.Empty(s.Push(new byte[AnnexBAccessUnitSplitter.MaxBufferedBytes + 1]));

        Assert.Null(s.Flush());
        Assert.Single(s.Push([.. Au(1, 5), .. Au(2, 5)]));
    }

    [Fact]
    public void New_encodings_are_video_with_the_stream_format_and_unique_ids()
    {
        var a = VideoEncodings.NewOutgoing();
        var b = VideoEncodings.NewOutgoing();

        Assert.Equal(EncodingType.Video, a.type);
        Assert.Equal(EncodingDir.Outgoing, a.dir);
        Assert.Equal(EncodingDir.Incoming, VideoEncodings.NewIncoming().dir);
        Assert.Equal((ushort)1280, a.data.video_enc.frame_width);
        Assert.Equal((ushort)3000, a.data.video_enc.max_bit_rate);
        Assert.NotEqual(a.id[0], b.id[0]);
    }
}
