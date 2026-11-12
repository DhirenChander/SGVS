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
    uint32_t bitrate = 64000;
    // Channels in the PCM passed to push_pcm. 1 (mono, copied to both sides) or 2.
    uint8_t inputChannels = 2;
    AudioContent content = AudioContent::General;
};

// Compresses raw 16-bit PCM into Opus packets at a fixed bitrate.
// Output matches what the networking module sends: Opus, 48 kHz, stereo, one packet per 10 ms.
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

    // Takes interleaved 16-bit PCM at 48 kHz. `frames` is samples per channel.
    // Any amount can be passed; leftover samples wait for the next call to fill a 10 ms packet.
    bool push_pcm(const int16_t *samples, size_t frames);

    // Safe to call from another thread. Takes effect from the next packet.
    // Returns false and keeps the current bitrate if the value is out of range.
    bool set_bitrate(uint32_t bitrate);
    uint32_t bitrate() const;

    // Drops any buffered samples that haven't made a full packet yet.
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

    bool encode_pending();
};
