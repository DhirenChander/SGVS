#include <unity.h>
#include "shared_mem_lib_core/ringbuff.h"
#include "string.h"

// Required to compile and use unity
void setUp(void) {}
void tearDown(void) {}

void test_ringbuff_push(void);
void test_ringbuff_try_pop(void);

int main(void)
{
    UNITY_BEGIN();
    RUN_TEST(test_ringbuff_push);
    RUN_TEST(test_ringbuff_try_pop);
    return UNITY_END();
}

void test_ringbuff_push(void)
{
    struct TestCase
    {
        Slot *data;
        uint8_t len;
        uint8_t size;
    };

    const size_t case_count = 2;
    struct TestCase cases[2] = {
        {.data = (Slot[]){
             {.bytes = (uint8_t[]){0x12, 0x43, 0xFF, 0x54}, .size = 4}},
         .len = 1,
         .size = 4},
        {.data = (Slot[]){{.bytes = (uint8_t[]){0x12, 0x43, 0xFF, 0x54}, .size = 4}, {.bytes = (uint8_t[]){0xF2, 0xF3}, .size = 2}, {.bytes = (uint8_t[]){0xA4}, .size = 1}}, .len = 3, .size = 4},
    };

    for (int i = 0; i < case_count; i++)
    {
        RingBuffer rb;
        rb.id = i + 0x1390289301 % 198023;
        TEST_ASSERT(open_ring(&rb, cases[i].size, cases[i].len + 1));

        TEST_ASSERT(push(&rb, cases[i].data, cases[i].len));

        TEST_ASSERT_EQUAL(cases[i].len, atomic_load(&rb.ring->head));
        TEST_ASSERT_EQUAL(0, atomic_load(&rb.ring->tail));

        for (int j = 0; j < cases[i].len; j++)
        {
            TEST_ASSERT_EQUAL(cases[i].data[j].size, rb.ring->buff[j].size);
            TEST_ASSERT_EQUAL_MEMORY(cases[i].data[j].bytes, rb.ring->buff[j].bytes, cases[i].data[j].size);
        }

        RingBuffer rb_2;
        rb_2.id = rb.id;
        open_ring(&rb_2, cases[i].size, cases[i].len);
        TEST_ASSERT_EQUAL_MEMORY(rb.ring, rb_2.ring, sizeof(Ring));
    }
}

void test_ringbuff_try_pop(void)
{
    struct TestCase
    {
        Slot *data;
        uint8_t len;
        uint8_t size;
    };

    const size_t case_count = 2;
    struct TestCase cases[2] = {
        {.data = (Slot[]){
             {.bytes = (uint8_t[]){0x12, 0x43, 0xFF, 0x54}, .size = 4}},
         .len = 1,
         .size = 4},
        {.data = (Slot[]){{.bytes = (uint8_t[]){0x12, 0x43, 0xFF, 0x54}, .size = 4}, {.bytes = (uint8_t[]){0xF2, 0xF3}, .size = 2}, {.bytes = (uint8_t[]){0xA4}, .size = 1}}, .len = 3, .size = 4},
    };

    for (int i = 0; i < case_count; i++)
    {
        RingBuffer rb;
        rb.id = i + 0x1391419301 % 1234023;
        TEST_ASSERT(open_ring(&rb, cases[i].size, cases[i].len + 1));

        TEST_ASSERT(push(&rb, cases[i].data, cases[i].len));

        RingBuffer rb_2;
        rb_2.id = rb.id;
        TEST_ASSERT(open_ring(&rb_2, cases[i].size, cases[i].len + 1));
        TEST_ASSERT_EQUAL_MEMORY(rb.ring, rb_2.ring, sizeof(Ring));

        Slot *out = (Slot *)malloc(cases[i].len * sizeof(Slot));

        for (int j = 0; j < cases[i].len; j++)
        {
            uint8_t *buff = (uint8_t *)malloc(cases[i].size);
            out[j].bytes = buff;
            out[j].size = cases[i].size;
        }

        uint32_t out_count = 0;

        TEST_ASSERT(try_pop(&rb_2, out, &out_count, cases[i].len));
        TEST_ASSERT_EQUAL(cases[i].len, out_count);
        TEST_ASSERT_EQUAL(cases[i].len, atomic_load(&rb_2.ring->head));
        TEST_ASSERT_EQUAL(cases[i].len, atomic_load(&rb_2.ring->tail));

        for (int j = 0; j < cases[i].len; j++)
        {
            TEST_ASSERT_EQUAL_MEMORY(cases[i].data[j].bytes, out[j].bytes, cases[i].data[j].size);
            TEST_ASSERT_EQUAL(cases[i].data[j].size, out[j].size);
        }

        Slot fail_out;
        fail_out.bytes = (uint8_t *)malloc(cases[i].size);
        uint32_t fout_count;
        TEST_ASSERT(!try_pop(&rb_2, &fail_out, &fout_count, 1));
    }
}
