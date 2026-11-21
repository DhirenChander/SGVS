using System.Diagnostics;
using System.Runtime.InteropServices;
using SharedMemLibrary;
using Encoding = SharedMemLibrary.Encoding;

namespace SGVS.Frontend.Services.Audio;

// Reads the Audio + Incoming ring that the networking module fills, one Opus packet per read.
// Only reads: it never requests or removes an encoding and never pushes frames.
// Not thread-safe: one reader per instance.
public sealed class SharedMemoryAudioSource(IFrameBuffer frameBuffer, Action<TimeSpan>? sleep = null)
    : IIncomingAudioSource
{
    // audio ring: 201 slots, one always empty (framebuff.c:46)
    public const int RingCapacity = 200;

    private readonly Action<TimeSpan> _sleep = sleep ?? Thread.Sleep;
    private Encoding? _encoding;

    public bool TryReadPacket(TimeSpan timeout, out ReadOnlyMemory<byte> packet)
    {
        packet = default;

        if (_encoding is not { } encoding)
        {
            if (FindIncomingAudio() is not { } found)
            {
                _sleep(timeout);
                return false;
            }

            encoding = found;
            _encoding = found;
            frameBuffer.AlignRingsToConfig();
            // whatever queued up before we started listening is stale: drop it so playback starts live
            // frameBuffer.TryPopFrames(encoding, RingCapacity);
        }

        var clock = Stopwatch.StartNew();
        var (frames, ok) = frameBuffer.BlockingPopFrames(encoding, 1, ToMilliseconds(timeout));
        if (!ok || frames.Length == 0)
        {
            if (!InConfig(encoding))
                _encoding = null; // the call ended or networking re-registered: look again next time

            // the pop can fail at once (e.g. the ring isn't there), so wait out the timeout instead of spinning
            var left = timeout - clock.Elapsed;
            if (left > TimeSpan.Zero)
                _sleep(left);
            return false;
        }

        var frame = frames[0];
        var size = frame.raw.size;
        if (size == 0 || size > (nuint)frame.Data.Length)
            return false;

        packet = frame.Data.AsMemory(0, (int)size);
        return true;
    }

    // The last match is the newest: the relay never removes encodings, and new ones fill the first empty slot.
    private Encoding? FindIncomingAudio()
    {
        var (encodings, ok) = frameBuffer.ReadEncodings(Config.MaxEncodings);
        if (!ok)
            return null;

        Encoding? found = null;
        foreach (var enc in encodings)
        {
            // read the fields directly: IsAudio() and IsIncoming() call into the native library
            if (enc.type == EncodingType.Audio && enc.dir == EncodingDir.Incoming)
                found = enc;
        }
        return found;
    }

    private bool InConfig(Encoding encoding)
    {
        var (encodings, ok) = frameBuffer.ReadEncodings(Config.MaxEncodings);
        if (!ok)
            return true; // can't tell, so keep it and try again
        foreach (var enc in encodings)
        {
            if (SameBytes(enc, encoding))
                return true;
        }
        return false;
    }

    // Byte comparison instead of Encoding.Equals, which calls into the native library.
    // Keeps working when Encoding.id changes type.
    private static bool SameBytes(Encoding a, Encoding b) =>
        MemoryMarshal.AsBytes(new ReadOnlySpan<Encoding>(in a))
            .SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<Encoding>(in b)));

    private static uint ToMilliseconds(TimeSpan timeout) =>
        (uint)Math.Clamp(timeout.TotalMilliseconds, 0, uint.MaxValue);
}
