// Partially Generated using Claude
#include "../audio_capture.hpp"
#include <gtest/gtest.h>
#include <vector>
#include <cstdint>
#include <numeric>

struct CbCall
{
    size_t head;
    uint32_t frames;
    size_t frameSize;
    std::vector<uint8_t> snapshot;
};

class ProcessAudioReaderTest : public ::testing::Test
{
protected:
    std::vector<CbCall> calls;
    ProcessAudioReader all_reader{};

    // Duplicate member functions because test fixtures do not have friend access to ProcessAudioReader's private fields
    static void setCb(ProcessAudioReader &r, Callback cb) { r.m_cb = std::move(cb); }
    static const uint8_t *bufferData(const ProcessAudioReader &r) { return r.m_buffer; }
    static uint32_t &head(ProcessAudioReader &r) { return r.m_buffHead; }
    static size_t bufferSize(const ProcessAudioReader &r) { return r.m_buffSize; }
    static void handle(ProcessAudioReader &r, uint8_t *d, uint32_t frames, size_t fs, uint32_t flags)
    {
        r.handle_audio_buffer(d, frames, fs, flags);
    }

    static void setupBuffer(ProcessAudioReader &r, AudioFormat format)
    {
        r.setup_buffer(format);
    }

    void SetUp() override
    {
        setupBuffer(all_reader, AudioFormat{true, 44100, 16});
        setCb(all_reader, [this](uint32_t head, uint16_t frames, size_t frameSize)
              {
            const uint8_t *p = bufferData(all_reader) + head;
            calls.push_back({head, frames, frameSize,
                             std::vector<uint8_t>(p, p + frames * frameSize)}); });
    }
};

TEST_F(ProcessAudioReaderTest, CopiesDataAndInvokesCallbackOnce)
{
    std::vector<uint8_t> in(16);
    std::iota(in.begin(), in.end(), 1);

    handle(all_reader, in.data(), 4, 4, 0);

    ASSERT_EQ(calls.size(), 1u);
    EXPECT_EQ(calls[0].head, 0u);
    EXPECT_EQ(calls[0].frames, 4u);
    EXPECT_EQ(calls[0].frameSize, 4u);
    EXPECT_EQ(calls[0].snapshot, in);
}

#ifdef _WIN32
TEST_F(ProcessAudioReaderTest, SilentFlagWritesZeros)
{
    std::vector<uint8_t> in(16, 0xFF);
    handle(all_reader, in.data(), 4, 4, AUDCLNT_BUFFERFLAGS_SILENT);
    ASSERT_EQ(calls.size(), 1u);
    EXPECT_EQ(calls[0].snapshot, std::vector<uint8_t>(16, 0));
}
#endif

TEST_F(ProcessAudioReaderTest, NullDataWritesZeros)
{
    handle(all_reader, nullptr, 4, 4, 0);
    ASSERT_EQ(calls.size(), 1u);
    EXPECT_EQ(calls[0].snapshot, std::vector<uint8_t>(16, 0));
}

TEST_F(ProcessAudioReaderTest, HeadAdvancesBetweenCalls)
{
    std::vector<uint8_t> in(16, 0xAB);
    handle(all_reader, in.data(), 4, 4, 0);
    handle(all_reader, in.data(), 4, 4, 0);

    ASSERT_EQ(calls.size(), 2u);
    EXPECT_EQ(calls[0].head, 0u);
    EXPECT_EQ(calls[1].head, 16u);
}

TEST_F(ProcessAudioReaderTest, WrapsAround)
{
    const size_t expectedBuffSize = 705600;
    const size_t slotSize = 441;
    const size_t slots = expectedBuffSize / slotSize;
    std::vector<uint8_t> in(slotSize, 0xCD);
    for (size_t i = 0; i < slots; ++i)
        handle(all_reader, in.data(), slotSize, 1, 0);
    handle(all_reader, in.data(), slotSize, 1, 0);

    EXPECT_EQ(calls.back().head, 0u);
}

// mono 16-bit at 4 Hz: a 32 byte ring
static const AudioFormat kTinyRing{false, 4, 16};

TEST_F(ProcessAudioReaderTest, PacketThatDoesNotFitAtTheEndStartsAtZero)
{
    setupBuffer(all_reader, kTinyRing);
    ASSERT_EQ(bufferSize(all_reader), 32u);
    std::vector<uint8_t> in(12);
    std::iota(in.begin(), in.end(), 1);

    handle(all_reader, in.data(), 3, 4, 0); // 0..12
    handle(all_reader, in.data(), 3, 4, 0); // 12..24
    handle(all_reader, in.data(), 3, 4, 0); // 24..36 runs past 32, so 0..12
    handle(all_reader, in.data(), 3, 4, 0);

    ASSERT_EQ(calls.size(), 4u);
    EXPECT_EQ(calls[2].head, 0u);
    EXPECT_EQ(calls[2].snapshot, in);
    EXPECT_EQ(calls[3].head, 12u);
}

TEST_F(ProcessAudioReaderTest, PacketBiggerThanTheRingIsDropped)
{
    setupBuffer(all_reader, kTinyRing);
    std::vector<uint8_t> little(4, 0xAB);
    std::vector<uint8_t> huge(36, 0xCD);

    handle(all_reader, little.data(), 1, 4, 0);
    handle(all_reader, huge.data(), 9, 4, 0);
    handle(all_reader, little.data(), 1, 4, 0);

    ASSERT_EQ(calls.size(), 2u);
    EXPECT_EQ(calls[1].head, 4u);
}

TEST_F(ProcessAudioReaderTest, BufferSizeIsCorrect)
{
    ProcessAudioReader r1{};
    setupBuffer(r1, AudioFormat{true, 44100, 16});
    EXPECT_EQ(bufferSize(r1), 705600);

    ProcessAudioReader r2{};
    setupBuffer(r2, AudioFormat{false, 96000, 24});
    EXPECT_EQ(bufferSize(r2), 1152000);

    ProcessAudioReader r3{};
    setupBuffer(r3, AudioFormat{});
    EXPECT_EQ(bufferSize(r3), 0);
}

#ifdef _WIN32
TEST_F(ProcessAudioReaderTest, WindowsWaveFormatTest)
{
    WAVEFORMATEX win_format1 = windows_format(AudioFormat{true, 44100, 16});
    EXPECT_EQ(win_format1.wFormatTag, WAVE_FORMAT_PCM);
    EXPECT_EQ(win_format1.nChannels, 2);
    EXPECT_EQ(win_format1.nSamplesPerSec, 44100);
    EXPECT_EQ(win_format1.wBitsPerSample, 16);
    EXPECT_EQ(win_format1.nBlockAlign, 4);
    EXPECT_EQ(win_format1.nAvgBytesPerSec, 176400);

    WAVEFORMATEX win_format2 = windows_format(AudioFormat{false, 96000, 24});
    EXPECT_EQ(win_format2.wFormatTag, WAVE_FORMAT_PCM);
    EXPECT_EQ(win_format2.nChannels, 1);
    EXPECT_EQ(win_format2.nSamplesPerSec, 96000);
    EXPECT_EQ(win_format2.wBitsPerSample, 24);
    EXPECT_EQ(win_format2.nBlockAlign, 3);
    EXPECT_EQ(win_format2.nAvgBytesPerSec, 288000);
}
#endif

int main(int argc, char **argv)
{
    testing::InitGoogleTest(&argc, argv);
    return RUN_ALL_TESTS();
}