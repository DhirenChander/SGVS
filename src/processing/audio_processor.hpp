#pragma once

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <vector>

// Defined in opus.h. Forward declared so users of this header don't need the Opus headers.
struct OpusEncoder;

// Called once per encoded packet, on the thread that called push_pcm.
// `data` is only valid during the call, so copy it if it needs to be kept.
// Calling push_pcm from inside the callback does nothing and returns false. set_bitrate and reset
// can be called from it and take effect from the next packet. Don't destroy the processor from
// inside the callback: push_pcm still uses it after the callback returns.
// If the callback throws, the exception comes out of push_pcm. That packet and the rest of that
// call's input are lost, but the processor stays usable. On a capture thread an uncaught exception
// ends the program, so catch it inside the callback.
using PacketCallback = std::function<void(const uint8_t *data, size_t size)>;

// What the audio is, so Opus can tune for it.
enum class AudioContent
{
    Voice,  // Microphone
    General // Desktop or application audio
};

struct AudioProcessorConfig
{
    // Bits per second. Held constant (CBR) and can be changed later with set_bitrate.
    // Every packet is (bitrate + 400) / 800 bytes: bitrate * 10 ms / 8, rounded to the nearest byte.
    // Below about 25 kbps Opus switches to mono to save bits, even for stereo input. Keep desktop
    // audio above that if stereo matters.
    uint32_t bitrate = 64000;
    // Channels in the PCM passed to push_pcm. 1 (mono, copied to both sides) or 2.
    // No default: set it from what the capture device delivers. Left at 0, the constructor throws.
    uint8_t inputChannels = 0;
    AudioContent content = AudioContent::General;
    // Sample rate of the PCM passed to push_pcm. Only 48000 is accepted, since the encoder doesn't
    // resample. No default: set it from the capture device so other rates are rejected instead of
    // playing back at the wrong speed. Left at 0, the constructor throws.
    uint32_t inputSampleRate = 0;
};

// Compresses raw 16-bit PCM into Opus packets at a fixed bitrate.
// Output matches what the networking module sends: Opus, 48 kHz, stereo, one packet per 10 ms.
//
// push_pcm and reset must be called from one thread at a time (normally the capture thread).
// Only set_bitrate and bitrate are safe to call from other threads. Mic and desktop audio each run
// on their own capture thread, so give each one its own processor.
class AudioProcessor
{
public:
    static constexpr uint32_t SAMPLE_RATE = 48000;
    static constexpr uint8_t CHANNELS = 2;
    static constexpr uint32_t FRAME_MS = 10;
    static constexpr size_t FRAME_SAMPLES = SAMPLE_RATE * FRAME_MS / 1000; // 480 per channel
    // Largest packet Opus can produce. Matches the audio slot size in shared memory.
    static constexpr size_t MAX_PACKET_SIZE = 1275;
    static constexpr uint32_t MIN_BITRATE = 6000;
    static constexpr uint32_t MAX_BITRATE = 510000;

    // Throws std::invalid_argument for a bad config, std::runtime_error if Opus fails to start.
    AudioProcessor(AudioProcessorConfig config, PacketCallback callback);
    ~AudioProcessor();

    AudioProcessor(const AudioProcessor &) = delete;
    AudioProcessor &operator=(const AudioProcessor &) = delete;

    // Takes interleaved 16-bit PCM at 48 kHz. `frames` is samples per channel, so `samples` must
    // hold frames * inputChannels values. The processor can't check this.
    // Any amount can be passed; leftover samples wait for the next call to fill a 10 ms packet.
    // A final partial packet is never sent (there is no flush), which is fine for live audio.
    // Returns false without using the input if `samples` is null or this is called from inside the
    // callback. Also returns false if Opus fails to encode: that 10 ms and the rest of the input
    // are dropped, and the processor stays usable.
    bool push_pcm(const int16_t *samples, size_t frames);

    // Safe to call from another thread. Takes effect from the next packet, including one that is
    // already partly buffered. Returns false and keeps the current bitrate if out of range.
    bool set_bitrate(uint32_t bitrate);
    // The last bitrate requested. The encoder switches to it when the next packet is encoded.
    uint32_t bitrate() const;

    // Starts a new stream: drops buffered samples that haven't made a full packet and clears the
    // encoder's history, so the next packet doesn't depend on earlier audio. Keeps the bitrate.
    void reset();

private:
    OpusEncoder *m_encoder = nullptr;
    PacketCallback m_cb;
    uint8_t m_inputChannels;

    // Requested bitrate (any thread) vs the one the encoder is using (encoding thread only)
    std::atomic<uint32_t> m_bitrate;
    uint32_t m_appliedBitrate;

    // One 10 ms stereo frame being filled
    std::vector<int16_t> m_pending;
    size_t m_pendingFrames = 0;
    uint8_t m_packet[MAX_PACKET_SIZE];
    // True while m_cb runs, so push_pcm can't overwrite m_packet underneath it
    bool m_inCallback = false;

    bool encode_pending();
};
