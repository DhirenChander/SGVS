#include "unity/unity.h"
#include "../../../src/shared_mem_lib/core/ringbuff.h"
#include "string.h"

// Required to compile and use unity
void setUp(void) {}
void tearDown(void) {}

void test_ringbuff_push(void);
//void test_ringbuff_pop(void);

int main(void) { 
    setvbuf(stdout, NULL, _IONBF, 0);
    puts("main reached");
    UNITY_BEGIN();
    RUN_TEST(test_ringbuff_push);
    //RUN_TEST(test_ringbuff_pop);
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
            push(&rb, cases[i].data[j], cases[i].sizes[j]);

        TEST_ASSERT(atomic_load(&rb.ring->head) == cases[i].len);
        TEST_ASSERT(atomic_load(&rb.ring->tail) == 0);

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

