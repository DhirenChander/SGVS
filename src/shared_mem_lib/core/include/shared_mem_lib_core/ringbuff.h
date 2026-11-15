#ifndef RINGBUFF_H

#if defined(_WIN32) || defined(_WIN64)
#include <windows.h>
#endif
#include <stdint.h>
#ifdef __cplusplus
#include <atomic>
#define atomic_uint_least32_t std::atomic_uint_least32_t
#else
#include <stdatomic.h>
#endif
#include <inttypes.h>
#include <stdbool.h>
#include "ring.h"

#ifdef __cplusplus
extern "C"
{
#endif

#define RINGBUFF_H

    typedef struct
    {
        Ring *ring;
        size_t *lens;
        uint8_t *data;
        uint64_t id;
#if defined(_WIN32) || defined(_WIN64)
        HANDLE map;
        HANDLE ev;
#endif
    } RingBuffer;

    bool push(RingBuffer *rb, Slot *data, uint32_t size);
    bool open_ring(RingBuffer *rb, uint64_t slot_size, uint32_t slot_count);
    bool try_pop(RingBuffer *rb, Slot *out, uint32_t *out_count, uint32_t max_slots_out);
    bool blocking_pop(RingBuffer *rb, Slot *out, uint32_t *out_count, uint32_t max_slots_out, uint32_t max_timeout);
    void free_ringbuffer(RingBuffer *rb);

#ifdef __cplusplus
}
#endif

#endif