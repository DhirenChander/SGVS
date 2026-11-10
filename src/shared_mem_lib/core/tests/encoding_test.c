#include <unity.h>
#include "shared_mem_lib_core/encoding.h"
#include "string.h"

// Required to compile and use unity
void setUp(void) {}
void tearDown(void) {}

void test_nil_encoding(void);
void test_new_audio_encoding(void);
void test_new_video_encoding(void);

int main(void)
{
    UNITY_BEGIN();
    RUN_TEST(test_nil_encoding);
    RUN_TEST(test_new_audio_encoding);
    RUN_TEST(test_new_video_encoding);
    return UNITY_END();
}

void test_nil_encoding(void)
{
    Encoding enc = nil_encoding();
    TEST_ASSERT_EQ(enc.id, 0);
}

void test_new_audio_encoding(void)
{
    struct TestCase
    {
        bool isInput;
    };

    const size_t case_count = 2;
    struct TestCase cases[case_count] = {
        (struct TestCase){.isInput = true},
        (struct TestCase){.isInput = false},
    };

    for (int i = 0; i < case_count; i++)
    {
        Encoding enc = new_audio_encoding(cases[i].isInput);
        TEST_ASSERT_NOT_EQUAL(enc.id, 0);
        TEST_ASSERT_EQUAL(enc.type, Audio);
        TEST_ASSERT_EQUAL(enc.data.audio_enc.is_input, cases[i].isInput);
    }
}

void test_new_video_encoding(void)
{
    struct TestCase
    {
        uint16_t maxBR;
        uint16_t minBR;
        uint16_t frameHeight;
        uint16_t frameWidth;
        uint8_t frameRate;
    };

    const size_t case_count = 2;
    struct TestCase cases[case_count] = {
        (struct TestCase){.maxBR = 44, .minBR = 22, .frameHeight = 1080, .frameWidth = 1920, .frameRate = 60},
    };

    for (int i = 0; i < case_count; i++)
    {
        Encoding enc = new_video_encoding(cases[i].maxBR, cases[i].minBR, cases[i].frameHeight, cases[i].frameWidth, cases[i].frameRate);
        TEST_ASSERT_NOT_EQUAL(enc.id, 0);
        TEST_ASSERT_EQUAL(enc.type, Video);
        TEST_ASSERT_EQUAL(enc.data.video_enc.max_bit_rate, cases[i].maxBR);
        TEST_ASSERT_EQUAL(enc.data.video_enc.min_bit_rate, cases[i].minBR);
        TEST_ASSERT_EQUAL(enc.data.video_enc.frame_height, cases[i].frameHeight);
        TEST_ASSERT_EQUAL(enc.data.video_enc.frame_width, cases[i].frameWidth);
        TEST_ASSERT_EQUAL(enc.data.video_enc.frame_rate, cases[i].frameRate);
    }
}