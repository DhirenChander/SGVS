using System.Diagnostics;
using System.Threading.Channels;
using SharedMemLibrary;

namespace SGVS.Frontend.Services.Audio;

// Reads the audio queue that the frontend's Receivers fill with frames from the Audio + Incoming ring,
// one Opus packet per read. Never calls into shared memory: it reads only raw.size and Data.
// Trusts the queue to hold only audio: a wrong frame fails to decode and the pump drops it.
// Make one per call, so the first read empties what's left from before. Not thread-safe: one reader per instance.
public sealed class ChannelAudioSource(ChannelReader<GCFrame> frames, Action<TimeSpan>? sleep = null)
    : IIncomingAudioSource
{
    private readonly Action<TimeSpan> _sleep = sleep ?? Thread.Sleep;
    private bool _drained;

    public bool TryReadPacket(TimeSpan timeout, out ReadOnlyMemory<byte> packet)
    {
        packet = default;

        if (!_drained)
        {
            // the queue lives as long as the app: whatever is in it is from before this call, so drop it
            while (frames.TryRead(out _))
            {
            }
            _drained = true;
        }

        if (!TryReadFrame(timeout, out var frame))
            return false;

        var size = frame.raw.size;
        if (size == 0 || size > (nuint)frame.Data.Length)
            return false;

        packet = frame.Data.AsMemory(0, (int)size);
        return true;
    }

    private bool TryReadFrame(TimeSpan timeout, out GCFrame frame)
    {
        if (frames.TryRead(out frame))
            return true;

        var clock = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            // blocks the pump thread, which has no synchronization context, so this can't deadlock.
            // AsTask first: a ValueTask must not be blocked on directly.
            if (frames.WaitToReadAsync(cts.Token).AsTask().GetAwaiter().GetResult())
                return frames.TryRead(out frame);
        }
        catch (OperationCanceledException)
        {
            return false; // nothing came in time
        }

        // the queue is closed: the wait returns at once every time, so wait out the timeout instead of spinning
        var left = timeout - clock.Elapsed;
        if (left > TimeSpan.Zero)
            _sleep(left);
        return false;
    }
}
