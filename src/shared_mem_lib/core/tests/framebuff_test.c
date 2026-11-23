#include <unity.h>
#include <string.h>
#include "shared_mem_lib_core/framebuff.h"

void setUp(void) {}
void tearDown(void) {}

void test_push_and_pop_message_between_framebuffers(void);

int main(void)
{
    UNITY_BEGIN();
    RUN_TEST(test_push_and_pop_message_between_framebuffers);
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