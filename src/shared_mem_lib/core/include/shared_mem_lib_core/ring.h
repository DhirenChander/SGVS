#ifndef RING_H

// Needed for MSVC support
#ifndef nullptr
#define nullptr NULL
#endif

#include <stdint.h>
#include <inttypes.h>

#ifdef __cplusplus
#include <atomic>
// Bring the type alias into the global namespace for C++ compatibility
using std::atomic_uint_least32_t;
#else
#include <stdatomic.h>
#endif

#define RING_H

typedef struct
{
    size_t size;
    uint8_t *bytes;
} Slot;

// Need a struct for the underlying data to ensure function definitions do not get included in the data mapping
typedef struct
{
    Slot *buff;
    atomic_uint_least32_t head;
    atomic_uint_least32_t tail;
    uint64_t slot_size;
    uint32_t slot_count; // One slot will always be left empty
} Ring;

#endif