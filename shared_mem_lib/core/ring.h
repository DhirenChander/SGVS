#ifndef RING_H

#include <windows.h>
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

#define SLOT_SIZE 500000
#define RING_SIZE 64

typedef struct {
    size_t size;
    uint8_t bytes[500000];
} Slot;

// Need a struct for the underlying data to ensure function definitions do not get included in the data mapping
typedef struct {
    Slot buff[RING_SIZE];
    atomic_uint_least32_t head;
    atomic_uint_least32_t tail;
} Ring;

typedef struct {
    Ring *ring;
    uint32_t id;
    #if defined(_WIN32) || defined(_WIN64)
    HANDLE map;
    HANDLE ev;
    #endif
} RingBuffer;

bool push(RingBuffer *rb, uint8_t* data, size_t size);
bool open_ring(RingBuffer *rb);

#endif