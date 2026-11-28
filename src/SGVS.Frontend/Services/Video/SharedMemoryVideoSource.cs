using System.Diagnostics;
using SharedMemLibrary;

namespace SGVS.Frontend.Services.Video;

// someone else's stream: the Video + Incoming ring the networking module fills, one access unit per slot.
// only reads, it never requests or removes an encoding. not thread-safe: one reader per instance
public sealed class SharedMemoryVideoSource(IFrameBuffer frameBuffer, Encoding encoding, Action<TimeSpan>? sleep = null)
    : IEncodedVideoSource
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(5);

    private readonly Action<TimeSpan> _sleep = sleep ?? Thread.Sleep;
    private bool _aligned;

    public bool TryReadAccessUnit(TimeSpan timeout, out ReadOnlyMemory<byte> accessUnit)
    {
        accessUnit = default;

        // the ring must be opened in this process before it can be read
        if (!_aligned)
        {
            frameBuffer.AlignRingsToConfig();
            _aligned = true;
        }

        var clock = Stopwatch.StartNew();
        var ms = (uint)Math.Clamp(timeout.TotalMilliseconds, 0, uint.MaxValue);
        var (frames, ok) = frameBuffer.BlockingPopFrames(encoding, 1, ms);
        if (!ok || frames.Length == 0)
        {
            // the pop can fail at once (e.g. the ring isn't there), so back off a little instead of spinning. not
            // the whole timeout: a failed pop can leave the ring's wake-up event set, and sleeping 100 ms would let
            // several frames pile up and play as a burst
            var left = timeout - clock.Elapsed;
            if (left > TimeSpan.Zero) _sleep(left < RetryDelay ? left : RetryDelay);
            return false;
        }

        var frame = frames[0];
        var size = frame.raw.size;
        if (size == 0 || size > (nuint)frame.Data.Length) return false;

        //TODO: Keep all frames if multiple exist
        accessUnit = frame.Data.AsMemory(0, (int)size);
        return true;
    }
}
