#ifndef RING_H

// Needed for MSVC support
#ifndef nullptr
#define nullptr NULL
#endif

#include <stdint.h>
#include <inttypes.h>
#include <stdatomic.h>

#define RING_H
typedef struct
{
    size_t len;
    uint8_t *bytes;
} Slot;
// Need a struct for the underlying data to ensure function definitions do not get included in the data mapping
typedef struct
{
    atomic_uint_least32_t head;
    atomic_uint_least32_t tail;
    uint64_t slot_size;
    uint32_t slot_count; // One slot will always be left empty
} Ring;

#endif