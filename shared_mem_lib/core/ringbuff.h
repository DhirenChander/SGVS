#ifndef RINGBUFF_H

#include <windows.h>
#include <stdint.h>
#include <stdatomic.h>
#include <inttypes.h>
#include <wchar.h>
#include "ring.h"

#define RINGBUFF_H

typedef struct
{
    Ring *ring;
    uint64_t id;
#if defined(_WIN32) || defined(_WIN64)
    HANDLE map;
    HANDLE ev;
#endif
} RingBuffer;

bool push(RingBuffer *rb, uint8_t *data, size_t size);
bool open_ring(RingBuffer *rb);
bool try_pop(RingBuffer *rb, uint8_t *out, size_t size);
bool blocking_pop(RingBuffer *rb, uint8_t *out, size_t size);

#endif