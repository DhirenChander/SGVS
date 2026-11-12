// Partially Generated using Claude
#include "../audio_processor.hpp"
#include <gtest/gtest.h>
#include <opus.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <random>
#include <stdexcept>
#include <string>
#include <thread>
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

    // Interleaved stereo with a different tone on each side
    std::vector<int16_t> stereo_sines(size_t frames, double leftFreq, double rightFreq, double amplitude = 8000.0)
    {
        auto left = sine(frames, 1, leftFreq, amplitude);
        auto right = sine(frames, 1, rightFreq, amplitude);
        std::vector<int16_t> out(frames * 2);
        for (size_t i = 0; i < frames; i++)
        {
            out[i * 2] = left[i];
            out[i * 2 + 1] = right[i];
        }
        return out;
    }

    // Opus rounds a CBR packet to the nearest whole byte: bitrate * 10 ms / 8, rounded
    size_t cbr_packet_size(uint32_t bitrate)
    {
        return (bitrate + 400) / 800;
    }

    // Pushes `pcm` in chunks of `chunk` frames
    void push_in_chunks(AudioProcessor &ap, const std::vector<int16_t> &pcm, uint8_t channels, size_t chunk)
    {
        const size_t total = pcm.size() / channels;
        for (size_t off = 0; off < total; off += chunk)
            ASSERT_TRUE(ap.push_pcm(pcm.data() + off * channels, std::min(chunk, total - off)));
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

    // Zero crossings per second on one channel of interleaved stereo, from frame `start` on.
    // A tone at f Hz crosses zero about 2f times a second.
    double crossing_rate(const std::vector<int16_t> &pcm, size_t channel, size_t start)
    {
        const size_t frames = pcm.size() / 2;
        size_t crossings = 0;
        for (size_t i = start + 1; i < frames; i++)
            if ((pcm[(i - 1) * 2 + channel] < 0) != (pcm[i * 2 + channel] < 0))
                crossings++;
        return crossings / ((frames - start) / (double)AudioProcessor::SAMPLE_RATE);
    }

    constexpr size_t WARMUP_FRAMES = AudioProcessor::FRAME_SAMPLES * 5; // First 50 ms of decoded audio
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

    AudioProcessorConfig highBitrate;
    highBitrate.bitrate = AudioProcessor::MAX_BITRATE + 1;
    EXPECT_THROW({ AudioProcessor ap(highBitrate, collect(packets)); }, std::invalid_argument);

    AudioProcessorConfig noChannels;
    noChannels.inputChannels = 0;
    EXPECT_THROW({ AudioProcessor ap(noChannels, collect(packets)); }, std::invalid_argument);

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

TEST(AudioProcessorTest, PacketSizeRoundsToNearestByte)
{
    // None of these divide by 800, so bitrate * 10 ms / 8 isn't a whole number of bytes
    for (uint32_t bitrate : {AudioProcessor::MIN_BITRATE, 6500u, 64500u, AudioProcessor::MAX_BITRATE})
    {
        Packets packets;
        AudioProcessorConfig config;
        config.bitrate = bitrate;
        AudioProcessor ap(config, collect(packets));

        auto pcm = sine(AudioProcessor::FRAME_SAMPLES * 10, 2);
        ASSERT_TRUE(ap.push_pcm(pcm.data(), AudioProcessor::FRAME_SAMPLES * 10));

        ASSERT_EQ(packets.size(), 10u);
        for (const auto &p : packets)
            EXPECT_EQ(p.size(), cbr_packet_size(bitrate)) << "bitrate " << bitrate;
    }

    // Spelled out for the limits: 7.5 bytes rounds up to 8, 637.5 to 638
    EXPECT_EQ(cbr_packet_size(AudioProcessor::MIN_BITRATE), 8u);
    EXPECT_EQ(cbr_packet_size(AudioProcessor::MAX_BITRATE), 638u);
    EXPECT_LE(cbr_packet_size(AudioProcessor::MAX_BITRATE), AudioProcessor::MAX_PACKET_SIZE);
}

TEST(AudioProcessorTest, SetBitrateAcceptsLimits)
{
    Packets packets;
    AudioProcessor ap({}, collect(packets));

    EXPECT_TRUE(ap.set_bitrate(AudioProcessor::MIN_BITRATE));
    EXPECT_EQ(ap.bitrate(), AudioProcessor::MIN_BITRATE);
    EXPECT_TRUE(ap.set_bitrate(AudioProcessor::MAX_BITRATE));
    EXPECT_EQ(ap.bitrate(), AudioProcessor::MAX_BITRATE);
}

TEST(AudioProcessorTest, StereoChannelsStaySeparate)
{
    Packets packets;
    AudioProcessor ap({}, collect(packets));

    // 500 Hz on the left, 1500 Hz on the right. Swapped or mixed channels would show up here.
    auto pcm = stereo_sines(AudioProcessor::SAMPLE_RATE, 500.0, 1500.0);
    ASSERT_TRUE(ap.push_pcm(pcm.data(), AudioProcessor::SAMPLE_RATE));

    for (const auto &p : packets)
        EXPECT_EQ(opus_packet_get_nb_channels(p.data()), 2);

    auto out = decode(packets);
    EXPECT_NEAR(crossing_rate(out, 0, WARMUP_FRAMES), 1000.0, 50.0);
    EXPECT_NEAR(crossing_rate(out, 1, WARMUP_FRAMES), 3000.0, 150.0);
}

TEST(AudioProcessorTest, LowBitrateStereoBecomesMono)
{
    // Not our code: below about 17 kbps (music) or 19 kbps (voice) Opus encodes stereo as mono
    // to save bits. Pinned here so a low set_bitrate losing the stereo image is no surprise.
    Packets packets;
    AudioProcessorConfig config;
    config.bitrate = 12000;
    AudioProcessor ap(config, collect(packets));

    auto pcm = stereo_sines(AudioProcessor::SAMPLE_RATE / 2, 500.0, 1500.0);
    ASSERT_TRUE(ap.push_pcm(pcm.data(), AudioProcessor::SAMPLE_RATE / 2));

    for (size_t i = 5; i < packets.size(); i++)
        EXPECT_EQ(opus_packet_get_nb_channels(packets[i].data()), 1) << "packet " << i;
}

TEST(AudioProcessorTest, ChunkSizeDoesNotChangeOutput)
{
    // Capture can hand over one frame at a time (desktop_audio_encoder does) or big uneven blocks.
    // However the input is split, the packets must be byte for byte the same.
    for (uint8_t channels : {(uint8_t)1, (uint8_t)2})
    {
        auto pcm = sine(AudioProcessor::SAMPLE_RATE / 2, channels);
        Packets reference;
        for (size_t chunk : {(size_t)AudioProcessor::SAMPLE_RATE / 2, (size_t)1, (size_t)137, (size_t)481})
        {
            Packets packets;
            AudioProcessorConfig config;
            config.inputChannels = channels;
            AudioProcessor ap(config, collect(packets));
            push_in_chunks(ap, pcm, channels, chunk);

            ASSERT_EQ(packets.size(), 50u) << "channels " << (int)channels << ", chunk " << chunk;
            if (reference.empty())
                reference = packets;
            else
                EXPECT_EQ(packets, reference) << "channels " << (int)channels << ", chunk " << chunk;
        }
    }
}

TEST(AudioProcessorTest, MonoInUnevenChunksKeepsTheSignal)
{
    Packets packets;
    AudioProcessorConfig config;
    config.inputChannels = 1;
    AudioProcessor ap(config, collect(packets));

    auto pcm = sine(AudioProcessor::SAMPLE_RATE, 1, 1000.0);
    push_in_chunks(ap, pcm, 1, 137);
    ASSERT_EQ(packets.size(), 100u);

    auto out = decode(packets);
    EXPECT_NEAR(crossing_rate(out, 0, WARMUP_FRAMES), 2000.0, 100.0);
    EXPECT_NEAR(crossing_rate(out, 1, WARMUP_FRAMES), 2000.0, 100.0);
}

TEST(AudioProcessorTest, SetBitrateMidFrameAppliesToThatPacket)
{
    Packets packets;
    AudioProcessor ap({}, collect(packets));

    // Half a packet is buffered when the bitrate changes. The packet it finishes uses the new rate.
    auto half = sine(AudioProcessor::FRAME_SAMPLES / 2, 2);
    ASSERT_TRUE(ap.push_pcm(half.data(), AudioProcessor::FRAME_SAMPLES / 2));
    ASSERT_TRUE(ap.set_bitrate(32000));
    ASSERT_TRUE(ap.push_pcm(half.data(), AudioProcessor::FRAME_SAMPLES / 2));

    ASSERT_EQ(packets.size(), 1u);
    EXPECT_EQ(packets[0].size(), cbr_packet_size(32000));
}

TEST(AudioProcessorTest, SetBitrateFromAnotherThread)
{
    Packets packets;
    AudioProcessor ap({}, collect(packets));

    // One thread flips the bitrate while this one encodes. Every packet must be a whole
    // packet at one of the two rates, never something in between.
    std::atomic<bool> done{false};
    std::thread changer([&]
                        {
                            bool low = false;
                            while (!done.load())
                            {
                                ap.set_bitrate(low ? 32000 : 64000);
                                low = !low;
                                std::this_thread::yield();
                            } });

    auto pcm = sine(AudioProcessor::SAMPLE_RATE * 2, 2);
    push_in_chunks(ap, pcm, 2, 137);
    done.store(true);
    changer.join();

    ASSERT_EQ(packets.size(), 200u);
    for (const auto &p : packets)
        EXPECT_TRUE(p.size() == cbr_packet_size(32000) || p.size() == cbr_packet_size(64000)) << p.size();

    // Once the other thread stops, the last value set wins
    ASSERT_TRUE(ap.set_bitrate(128000));
    ASSERT_TRUE(ap.push_pcm(pcm.data(), AudioProcessor::FRAME_SAMPLES));
    EXPECT_EQ(packets.back().size(), cbr_packet_size(128000));
}

TEST(AudioProcessorTest, CallbackExceptionLeavesProcessorUsable)
{
    Packets packets;
    bool fail = true;
    AudioProcessor ap({}, [&](const uint8_t *data, size_t size)
                      {
                          if (fail)
                              throw std::runtime_error("callback failed");
                          packets.emplace_back(data, data + size); });

    auto pcm = sine(AudioProcessor::FRAME_SAMPLES, 2);
    EXPECT_THROW(ap.push_pcm(pcm.data(), AudioProcessor::FRAME_SAMPLES), std::runtime_error);

    // The failed frame is gone, and the next full frame makes exactly one packet
    fail = false;
    ASSERT_TRUE(ap.push_pcm(pcm.data(), AudioProcessor::FRAME_SAMPLES));
    ASSERT_EQ(packets.size(), 1u);
    EXPECT_EQ(packets[0].size(), cbr_packet_size(64000));
}

TEST(AudioProcessorTest, LoudInputKeepsConstantSize)
{
    // Full-scale noise and a clipped square wave are the hardest input to fit into a fixed budget
    std::mt19937 rng(13);
    std::uniform_int_distribution<int> dist(INT16_MIN, INT16_MAX);
    std::vector<int16_t> noise(AudioProcessor::FRAME_SAMPLES * 20 * 2);
    for (auto &s : noise)
        s = (int16_t)dist(rng);

    std::vector<int16_t> square(AudioProcessor::FRAME_SAMPLES * 20 * 2);
    for (size_t i = 0; i < square.size(); i++)
        square[i] = (i / 2 / 24) % 2 ? INT16_MAX : INT16_MIN; // 1 kHz

    for (uint32_t bitrate : {AudioProcessor::MIN_BITRATE, 32000u, AudioProcessor::MAX_BITRATE})
    {
        for (const auto *pcm : {&noise, &square})
        {
            Packets packets;
            AudioProcessorConfig config;
            config.bitrate = bitrate;
            AudioProcessor ap(config, collect(packets));
            ASSERT_TRUE(ap.push_pcm(pcm->data(), pcm->size() / 2));

            ASSERT_EQ(packets.size(), 20u);
            for (const auto &p : packets)
                EXPECT_EQ(p.size(), cbr_packet_size(bitrate)) << "bitrate " << bitrate;
        }
    }
}

TEST(AudioProcessorTest, EveryContentAndChannelCombinationWorks)
{
    for (AudioContent content : {AudioContent::Voice, AudioContent::General})
    {
        for (uint8_t channels : {(uint8_t)1, (uint8_t)2})
        {
            Packets packets;
            AudioProcessorConfig config;
            config.content = content;
            config.inputChannels = channels;
            AudioProcessor ap(config, collect(packets));

            auto pcm = sine(AudioProcessor::SAMPLE_RATE / 2, channels);
            ASSERT_TRUE(ap.push_pcm(pcm.data(), AudioProcessor::SAMPLE_RATE / 2));

            const std::string label = std::string(content == AudioContent::Voice ? "voice" : "general") +
                                      ", channels " + std::to_string(channels);
            ASSERT_EQ(packets.size(), 50u) << label;
            for (const auto &p : packets)
                EXPECT_EQ(p.size(), cbr_packet_size(64000)) << label;

            auto out = decode(packets);
            EXPECT_NEAR(crossing_rate(out, 0, WARMUP_FRAMES), 2000.0, 100.0) << label;
        }
    }
}
