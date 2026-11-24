#include <unity.h>
#include <string.h>
#include "shared_mem_lib_core/framebuff.h"

void setUp(void) {}
void tearDown(void) {}

void test_push_and_pop_message_between_framebuffers(void);
void test_blocking_pop_on_one_ring_does_not_block_push_on_another(void);

int main(void)
{
    UNITY_BEGIN();
    RUN_TEST(test_push_and_pop_message_between_framebuffers);
    RUN_TEST(test_blocking_pop_on_one_ring_does_not_block_push_on_another);
    return UNITY_END();
}

void test_push_and_pop_message_between_framebuffers(void)
{
    FrameBuffer sender;
    FrameBuffer receiver;

    TEST_ASSERT_TRUE(new_framebuffer(&sender));
    TEST_ASSERT_TRUE(new_framebuffer(&receiver));

    Message drained;
    while (blocking_pop_message(&receiver, Application, Network, &drained, 0))
    {
    }

    Message msg = {0};
    msg.src = Application;
    msg.dest = Network;
    msg.opcode = START_HOST;
    msg.data[0] = 0xAB;
    msg.data[1] = 0xCD;

    TEST_ASSERT_TRUE(push_message(&sender, &msg));

    Message out = {0};
    TEST_ASSERT_TRUE(blocking_pop_message(&receiver, Application, Network, &out, 1000));

    TEST_ASSERT_EQUAL(msg.src, out.src);
    TEST_ASSERT_EQUAL(msg.dest, out.dest);
    TEST_ASSERT_EQUAL(msg.opcode, out.opcode);
    TEST_ASSERT_EQUAL_UINT8_ARRAY(msg.data, out.data, sizeof(msg.data));

    free_framebuffer(&receiver);
    free_framebuffer(&sender);
}

typedef struct
{
    FrameBuffer *fb;
    Encoding enc;
} PopArgs;

static DWORD WINAPI blocking_pop_thread(LPVOID param)
{
    PopArgs *args = (PopArgs *)param;
    uint8_t buf[1275];
    Frame frame = {0};
    frame.data = buf;
    frame.size = sizeof(buf);
    uint32_t count = 0;

    // Ring is empty, so this waits out the whole timeout
    blocking_pop_frames(args->fb, args->enc, &frame, &count, 1, 2000);
    return 0;
}

void test_blocking_pop_on_one_ring_does_not_block_push_on_another(void)
{
    FrameBuffer fb;
    TEST_ASSERT_TRUE(new_framebuffer(&fb));

    Encoding popped = new_audio_encoding(true);
    set_encoding_incoming(&popped);
    Encoding pushed = new_audio_encoding(true);
    set_encoding_outgoing(&pushed);
    TEST_ASSERT_TRUE(request_encoding(&fb, popped));
    TEST_ASSERT_TRUE(request_encoding(&fb, pushed));

    PopArgs args = {&fb, popped};
    HANDLE thread = CreateThread(NULL, 0, blocking_pop_thread, &args, 0, NULL);
    TEST_ASSERT_NOT_NULL(thread);
    Sleep(100); // let the thread start waiting

    uint8_t data[8] = {1, 2, 3, 4, 5, 6, 7, 8};
    Frame frame = {0};
    frame.encoding = pushed;
    frame.data = data;
    frame.size = sizeof(data);

    ULONGLONG start = GetTickCount64();
    bool ok = push_frames(&fb, &frame, 1);
    ULONGLONG elapsed = GetTickCount64() - start;

    TEST_ASSERT_TRUE(ok);
    TEST_ASSERT_LESS_THAN_UINT64(200, elapsed);

    WaitForSingleObject(thread, 5000);
    CloseHandle(thread);
    free_framebuffer(&fb);
}
