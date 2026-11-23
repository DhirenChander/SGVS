namespace SGVS.Frontend.Services.Audio;

// Incoming Opus packets, one at a time.
public interface IIncomingAudioSource
{
    // Waits up to timeout. False if no packet came (no incoming audio yet, or none in time).
    bool TryReadPacket(TimeSpan timeout, out ReadOnlyMemory<byte> packet);
}
