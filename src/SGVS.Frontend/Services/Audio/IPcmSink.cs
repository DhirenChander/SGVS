namespace SGVS.Frontend.Services.Audio;

// Where decoded call audio goes. Carter's player implements this. Called on the pump's background thread.
public interface IPcmSink
{
    // One 10 ms chunk: 48 kHz, 16-bit, stereo, interleaved L R (1920 bytes).
    // pcm is valid only during the call, because the pump reuses its buffer: copy it.
    // Must not block, or IncomingAudioPump.Stop can hang.
    void Enqueue(ReadOnlySpan<byte> pcm);
}
