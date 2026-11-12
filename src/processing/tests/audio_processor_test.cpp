// Partially Generated using Claude
#include "../audio_processor.hpp"
#include <gtest/gtest.h>
#include <opus.h>

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <stdexcept>
#include <vector>

namespace
{
    using Packets = std::vector<std::vector<uint8_t>>;

    constexpr double PI = 3.14159265358979323846;

    // Interleaved 16-bit sine wave, same value on every channel
    std::vector<int16_t> sine(size_t frames, uint8_t channels, double freq = 1000.0, double amplitude = 8000.0)
    {
        std::vector<int16_t> out(frames * channels);
        for (size_t i = 0; i < frames; i++)
        {
            int16_t v = (int16_t)std::lround(amplitude * std::sin(2.0 * PI * freq * i / AudioProcessor::SAMPLE_RATE));
            for (uint8_t c = 0; c < channels; c++)
                out[i * channels + c] = v;
        }
        return out;
    }

    PacketCallback collect(Packets &packets)
    {
        return [&packets](const uint8_t *data, size_t size)
        { packets.emplace_back(data, data + size); };
    }

    size_t cbr_packet_size(uint32_t bitrate)
    {
        return bitrate / 8 * AudioProcessor::FRAME_MS / 1000;
    }

    // Decodes packets back to interleaved stereo PCM
    std::vector<int16_t> decode(const Packets &packets)
    {
        int err = OPUS_OK;
        OpusDecoder *dec = opus_decoder_create(AudioProcessor::SAMPLE_RATE, AudioProcessor::CHANNELS, &err);
        EXPECT_EQ(err, OPUS_OK);

        std::vector<int16_t> out;
        std::vector<int16_t> buf(5760 * AudioProcessor::CHANNELS); // Largest Opus frame, 120 ms
        for (const auto &p : packets)
        {
            int n = opus_decode(dec, p.data(), (opus_int32)p.size(), buf.data(), 5760, 0);
            EXPECT_EQ(n, (int)AudioProcessor::FRAME_SAMPLES);
            if (n > 0)
                out.insert(out.end(), buf.begin(), buf.begin() + n * AudioProcessor::CHANNELS);
        }
        opus_decoder_destroy(dec);
        return out;
    }

    // RMS of one channel of interleaved stereo, from frame `start` on
    double rms(const std::vector<int16_t> &pcm, size_t channel, size_t start)
    {
        double sum = 0;
        size_t count = 0;
        for (size_t i = start * 2 + channel; i < pcm.size(); i += 2)
        {
            sum += (double)pcm[i] * pcm[i];
            count++;
        }
        return count ? std::sqrt(sum / count) : 0.0;
    }
}

TEST(AudioProcessorTest, OnePacketPer10ms)
{
    Packets packets;
    AudioProcessor ap({}, collect(packets));

    auto pcm = sine(AudioProcessor::SAMPLE_RATE, 2); // 1 second
    ASSERT_TRUE(ap.push_pcm(pcm.data(), AudioProcessor::SAMPLE_RATE));

    EXPECT_EQ(packets.size(), 100u);
}

TEST(AudioProcessorTest, PacketsAreConstantSizeForBitrate)
{
    for (uint32_t bitrate : {32000u, 64000u, 128000u})
    {
        Packets packets;
        AudioProcessorConfig config;
        config.bitrate = bitrate;
        AudioProcessor ap(config, collect(packets));

        auto pcm = sine(AudioProcessor::FRAME_SAMPLES * 20, 2);
        ASSERT_TRUE(ap.push_pcm(pcm.data(), AudioProcessor::FRAME_SAMPLES * 20));

        ASSERT_EQ(packets.size(), 20u);
        for (const auto &p : packets)
            EXPECT_EQ(p.size(), cbr_packet_size(bitrate)) << "bitrate " << bitrate;
    }
}

TEST(AudioProcessorTest, SilenceKeepsConstantSize)
{
    Packets packets;
    AudioProcessor ap({}, collect(packets));

    std::vector<int16_t> silence(AudioProcessor::FRAME_SAMPLES * 10 * 2, 0);
    ASSERT_TRUE(ap.push_pcm(silence.data(), AudioProcessor::FRAME_SAMPLES * 10));

    ASSERT_EQ(packets.size(), 10u);
    for (const auto &p : packets)
        EXPECT_EQ(p.size(), cbr_packet_size(64000));
}

TEST(AudioProcessorTest, BuffersChunksThatDontLineUpWith10ms)
{
    Packets packets;
    AudioProcessor ap({}, collect(packets));

    // Capture hands over arbitrary frame counts. 4900 frames = 10 packets + 100 left over.
    const size_t total = AudioProcessor::FRAME_SAMPLES * 10 + 100;
    auto pcm = sine(total, 2);
    for (size_t off = 0; off < total; off += 137)
    {
        size_t n = std::min<size_t>(137, total - off);
        ASSERT_TRUE(ap.push_pcm(pcm.data() + off * 2, n));
    }

    EXPECT_EQ(packets.size(), 10u);

    // The leftover 100 frames finish a packet once 380 more arrive
    auto more = sine(AudioProcessor::FRAME_SAMPLES - 100, 2);
    ASSERT_TRUE(ap.push_pcm(more.data(), AudioProcessor::FRAME_SAMPLES - 100));
    EXPECT_EQ(packets.size(), 11u);
}

TEST(AudioProcessorTest, ResetDropsPartialFrame)
{
    Packets packets;
    AudioProcessor ap({}, collect(packets));

    auto pcm = sine(300, 2);
    ASSERT_TRUE(ap.push_pcm(pcm.data(), 300));
    ap.reset();
    ASSERT_TRUE(ap.push_pcm(pcm.data(), 300));
    EXPECT_EQ(packets.size(), 0u);

    auto rest = sine(AudioProcessor::FRAME_SAMPLES - 300, 2);
    ASSERT_TRUE(ap.push_pcm(rest.data(), AudioProcessor::FRAME_SAMPLES - 300));
    EXPECT_EQ(packets.size(), 1u);
}

TEST(AudioProcessorTest, DecodesBackToSameSignal)
{
    Packets packets;
    AudioProcessor ap({}, collect(packets));

    const double amplitude = 8000.0;
    auto pcm = sine(AudioProcessor::SAMPLE_RATE, 2, 1000.0, amplitude);
    ASSERT_TRUE(ap.push_pcm(pcm.data(), AudioProcessor::SAMPLE_RATE));

    auto out = decode(packets);
    ASSERT_EQ(out.size(), pcm.size());

    // Skip the first 50 ms while the encoder warms up
    const size_t skip = AudioProcessor::FRAME_SAMPLES * 5;
    const double expected = amplitude / std::sqrt(2.0);
    EXPECT_NEAR(rms(out, 0, skip), expected, expected * 0.2);
    EXPECT_NEAR(rms(out, 1, skip), expected, expected * 0.2);

    // A 1 kHz tone crosses zero about 2000 times a second
    size_t crossings = 0;
    for (size_t i = skip + 1; i < out.size() / 2; i++)
        if ((out[(i - 1) * 2] < 0) != (out[i * 2] < 0))
            crossings++;
    const double seconds = (out.size() / 2 - skip) / (double)AudioProcessor::SAMPLE_RATE;
    EXPECT_NEAR(crossings / seconds, 2000.0, 100.0);
}

TEST(AudioProcessorTest, MonoInputGoesToBothChannels)
{
    Packets packets;
    AudioProcessorConfig config;
    config.inputChannels = 1;
    config.content = AudioContent::Voice;
    AudioProcessor ap(config, collect(packets));

    auto pcm = sine(AudioProcessor::SAMPLE_RATE / 2, 1); // 0.5 seconds
    ASSERT_TRUE(ap.push_pcm(pcm.data(), AudioProcessor::SAMPLE_RATE / 2));
    ASSERT_EQ(packets.size(), 50u);

    auto out = decode(packets);
    const size_t skip = AudioProcessor::FRAME_SAMPLES * 5;
    double left = rms(out, 0, skip);
    double right = rms(out, 1, skip);
    EXPECT_GT(left, 1000.0);
    EXPECT_NEAR(left, right, left * 0.05);
}

TEST(AudioProcessorTest, SetBitrateAppliesToNextPacket)
{
    Packets packets;
    AudioProcessor ap({}, collect(packets));

    auto pcm = sine(AudioProcessor::FRAME_SAMPLES * 2, 2);
    ASSERT_TRUE(ap.push_pcm(pcm.data(), AudioProcessor::FRAME_SAMPLES * 2));

    ASSERT_TRUE(ap.set_bitrate(32000));
    EXPECT_EQ(ap.bitrate(), 32000u);
    ASSERT_TRUE(ap.push_pcm(pcm.data(), AudioProcessor::FRAME_SAMPLES * 2));

    ASSERT_EQ(packets.size(), 4u);
    EXPECT_EQ(packets[0].size(), cbr_packet_size(64000));
    EXPECT_EQ(packets[1].size(), cbr_packet_size(64000));
    EXPECT_EQ(packets[2].size(), cbr_packet_size(32000));
    EXPECT_EQ(packets[3].size(), cbr_packet_size(32000));
}

TEST(AudioProcessorTest, SetBitrateRejectsOutOfRange)
{
    Packets packets;
    AudioProcessor ap({}, collect(packets));

    EXPECT_FALSE(ap.set_bitrate(AudioProcessor::MIN_BITRATE - 1));
    EXPECT_FALSE(ap.set_bitrate(AudioProcessor::MAX_BITRATE + 1));
    EXPECT_EQ(ap.bitrate(), 64000u);
}

TEST(AudioProcessorTest, RejectsBadConfig)
{
    Packets packets;

    AudioProcessorConfig lowBitrate;
    lowBitrate.bitrate = 1000;
    EXPECT_THROW({ AudioProcessor ap(lowBitrate, collect(packets)); }, std::invalid_argument);

    AudioProcessorConfig threeChannels;
    threeChannels.inputChannels = 3;
    EXPECT_THROW({ AudioProcessor ap(threeChannels, collect(packets)); }, std::invalid_argument);

    EXPECT_THROW({ AudioProcessor ap({}, nullptr); }, std::invalid_argument);
}

TEST(AudioProcessorTest, NullSamples)
{
    Packets packets;
    AudioProcessor ap({}, collect(packets));

    EXPECT_FALSE(ap.push_pcm(nullptr, 10));
    EXPECT_TRUE(ap.push_pcm(nullptr, 0));
    EXPECT_EQ(packets.size(), 0u);
}
