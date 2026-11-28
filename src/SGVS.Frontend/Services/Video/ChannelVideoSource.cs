using System.Threading.Channels;

namespace SGVS.Frontend.Services.Video;

// your own stream looping back to your own screen: the publisher writes access units, the pipeline reads them
public sealed class ChannelVideoSource(ChannelReader<byte[]> reader) : IEncodedVideoSource
{
    public bool TryReadAccessUnit(TimeSpan timeout, out ReadOnlyMemory<byte> accessUnit)
    {
        accessUnit = default;

        if (reader.TryRead(out var au))
        {
            accessUnit = au;
            return true;
        }

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            // blocks the pipeline thread, which has no synchronization context. AsTask first: a ValueTask must not be blocked on directly
            if (reader.WaitToReadAsync(cts.Token).AsTask().GetAwaiter().GetResult() && reader.TryRead(out au))
            {
                accessUnit = au;
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            // nothing came in time
        }

        return false;
    }
}
