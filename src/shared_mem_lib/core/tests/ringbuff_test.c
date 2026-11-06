#include <unity.h>
#include "shared_mem_lib_core/ringbuff.h"
#include "string.h"

// Required to compile and use unity
void setUp(void) {}
void tearDown(void) {}

void test_ringbuff_push(void);
void test_ringbuff_try_pop(void);

int main(void) { 
    UNITY_BEGIN();
    RUN_TEST(test_ringbuff_push);
    RUN_TEST(test_ringbuff_try_pop);
    return UNITY_END();
}

void test_ringbuff_push(void) {
    struct TestCase {
        uint8_t **data;
        uint8_t len;
        size_t *sizes;
    };

    const size_t case_count = 2;
    struct TestCase cases[2] = {
        (struct TestCase){.data = (uint8_t*[]){ (uint8_t[]){ 0x12, 0x43, 0xFF, 0x54 } }, .len = 1, .sizes = (size_t[]){ 4 }},
        (struct TestCase){.data = (uint8_t*[]){ (uint8_t[]){ 0x12, 0x43, 0xFF, 0x54 }, (uint8_t[]){ 0xF2, 0xF3 }, (uint8_t[]){ 0xA4 } }, .len = 3, .sizes = (size_t[]){ 4, 2, 1 }},
    };

    for(int i = 0; i  < case_count; i++) {
        RingBuffer rb;
        rb.id = i + 0x1390289301 % 198023;
        open_ring(&rb);

        for(int j = 0; j < cases[i].len; j++) 
        {
            push(&rb, cases[i].data[j], cases[i].sizes[j]);
            TEST_ASSERT(atomic_load(&rb.ring->head) == j+1);
            TEST_ASSERT(atomic_load(&rb.ring->tail) == 0);
        }

        for(int j = 0; j < cases[i].len; j++)
        {
            TEST_ASSERT(rb.ring->buff[j].size == cases[i].sizes[j]);
            TEST_ASSERT_EQUAL_MEMORY(cases[i].data[j], rb.ring->buff[j].bytes, cases[i].sizes[j]);
        }

        RingBuffer rb_2;
        rb_2.id = rb.id;
        open_ring(&rb_2);
        TEST_ASSERT_EQUAL_MEMORY(rb.ring, rb_2.ring, sizeof(Ring));
    }
}

void test_ringbuff_try_pop(void) {
    struct TestCase {
        uint8_t **data;
        uint8_t len;
        size_t *sizes;

    };

    const size_t case_count = 2;
    struct TestCase cases[2] = {
        (struct TestCase){.data = (uint8_t*[]){ (uint8_t[]){ 0x12, 0x43, 0xFF, 0x54 } }, .len = 1, .sizes = (size_t[]){ 4 }},
        (struct TestCase){.data = (uint8_t*[]){ (uint8_t[]){ 0x12, 0x43, 0xFF, 0x54 }, (uint8_t[]){ 0xF2, 0xF3 }, (uint8_t[]){ 0xA4 } }, .len = 3, .sizes = (size_t[]){ 4, 2, 1 }},
    };

    for(int i = 0; i  < case_count; i++) {
        RingBuffer rb;

        rb.id = i + 0x1391419301 % 1234023;
        open_ring(&rb);

        for(int j = 0; j < cases[i].len; j++) 
            push(&rb, cases[i].data[j], cases[i].sizes[j]);

        for(int j = 0; j < cases[i].len; j++)
        {
            uint8_t *out = (uint8_t*)malloc(cases[i].sizes[j]);
            bool result = try_pop(&rb, out, cases[i].sizes[j]);
            TEST_ASSERT(result);
            TEST_ASSERT_EQUAL_MEMORY(out, cases[i].data[j], cases[i].sizes[j]);
            TEST_ASSERT(atomic_load(&rb.ring->head) == cases[i].len);
            TEST_ASSERT(atomic_load(&rb.ring->tail) == j + 1);
        }

        uint8_t *fail_out;
        bool fail_result = try_pop(&rb, fail_out, 1000);
        TEST_ASSERT(!fail_result);

        RingBuffer rb_2;
        rb_2.id = rb.id;
        open_ring(&rb_2);
        TEST_ASSERT_EQUAL_MEMORY(rb.ring, rb_2.ring, sizeof(Ring));
    }
}

