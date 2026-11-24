using System.Security.Cryptography;
using SharedMemLibrary;

namespace SGVS.Frontend.Services.Video;

// the screen stream's shared memory encoding. built here instead of through new_video_encoding so it
// needs no native call, which keeps it usable in tests and off Windows
public static class VideoEncodings
{
    public static Encoding NewOutgoing() => New(EncodingDir.Outgoing);

    public static Encoding NewIncoming() => New(EncodingDir.Incoming);

    private static Encoding New(EncodingDir dir)
    {
        var enc = new Encoding { type = EncodingType.Video, dir = dir };
        enc.data.video_enc.max_bit_rate = VideoFormat.BitrateKbps;
        enc.data.video_enc.min_bit_rate = VideoFormat.BitrateKbps;
        enc.data.video_enc.frame_width = VideoFormat.Width;
        enc.data.video_enc.frame_height = VideoFormat.Height;
        enc.data.video_enc.frame_rate = VideoFormat.FrameRate;

        var random = RandomNumberGenerator.GetBytes(4 * sizeof(ulong));
        for (var i = 0; i < 4; i++)
            enc.id[i] = BitConverter.ToUInt64(random, i * sizeof(ulong));
        return enc;
    }
}
