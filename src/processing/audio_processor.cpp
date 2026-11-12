#include "audio_processor.hpp"

#include <opus.h>

#include <algorithm>
#include <cstdio>
#include <cstring>
#include <stdexcept>
#include <string>

static bool bitrate_in_range(uint32_t bitrate)
{
    return bitrate >= AudioProcessor::MIN_BITRATE && bitrate <= AudioProcessor::MAX_BITRATE;
}

AudioProcessor::AudioProcessor(AudioProcessorConfig config, PacketCallback callback)
    : m_cb(std::move(callback)),
      m_inputChannels(config.inputChannels),
      m_bitrate(config.bitrate),
      m_appliedBitrate(config.bitrate),
      m_pending(FRAME_SAMPLES * CHANNELS, 0)
{
    if (!m_cb)
        throw std::invalid_argument("AudioProcessor needs a packet callback");
    if (m_inputChannels != 1 && m_inputChannels != 2)
        throw std::invalid_argument("AudioProcessor input must be mono or stereo");
    if (!bitrate_in_range(config.bitrate))
        throw std::invalid_argument("AudioProcessor bitrate out of range: " + std::to_string(config.bitrate));

    int application = config.content == AudioContent::Voice ? OPUS_APPLICATION_VOIP : OPUS_APPLICATION_AUDIO;

    int err = OPUS_OK;
    m_encoder = opus_encoder_create(SAMPLE_RATE, CHANNELS, application, &err);
    if (err != OPUS_OK || m_encoder == nullptr)
        throw std::runtime_error(std::string("Failed to create Opus encoder: ") + opus_strerror(err));

    // Hard CBR: every packet is the same size, bitrate * 10 ms / 8 bytes rounded to the nearest byte
    // (80 bytes at 64 kbps, 8 at 6 kbps)
    opus_encoder_ctl(m_encoder, OPUS_SET_VBR(0));
    opus_encoder_ctl(m_encoder, OPUS_SET_BITRATE((opus_int32)config.bitrate));
}

AudioProcessor::~AudioProcessor()
{
    if (m_encoder != nullptr)
        opus_encoder_destroy(m_encoder);
}

bool AudioProcessor::push_pcm(const int16_t *samples, size_t frames)
{
    if (frames == 0)
        return true;
    if (samples == nullptr)
        return false;

    while (frames > 0)
    {
        size_t take = std::min(frames, FRAME_SAMPLES - m_pendingFrames);
        int16_t *dst = m_pending.data() + m_pendingFrames * CHANNELS;

        if (m_inputChannels == CHANNELS)
        {
            std::memcpy(dst, samples, take * CHANNELS * sizeof(int16_t));
        }
        else
        {
            // Mono: same sample on both sides
            for (size_t i = 0; i < take; i++)
            {
                dst[i * 2] = samples[i];
                dst[i * 2 + 1] = samples[i];
            }
        }

        samples += take * m_inputChannels;
        frames -= take;
        m_pendingFrames += take;

        if (m_pendingFrames == FRAME_SAMPLES && !encode_pending())
            return false;
    }

    return true;
}

bool AudioProcessor::encode_pending()
{
    m_pendingFrames = 0;

    uint32_t requested = m_bitrate.load();
    if (requested != m_appliedBitrate)
    {
        opus_encoder_ctl(m_encoder, OPUS_SET_BITRATE((opus_int32)requested));
        m_appliedBitrate = requested;
    }

    opus_int32 len = opus_encode(m_encoder, m_pending.data(), (int)FRAME_SAMPLES,
                                 m_packet, (opus_int32)MAX_PACKET_SIZE);
    if (len < 0)
    {
        fprintf(stderr, "Opus encode failed: %s\n", opus_strerror(len));
        return false;
    }

    m_cb(m_packet, (size_t)len);
    return true;
}

bool AudioProcessor::set_bitrate(uint32_t bitrate)
{
    if (!bitrate_in_range(bitrate))
        return false;
    m_bitrate.store(bitrate);
    return true;
}

uint32_t AudioProcessor::bitrate() const
{
    return m_bitrate.load();
}

void AudioProcessor::reset()
{
    m_pendingFrames = 0;
}
