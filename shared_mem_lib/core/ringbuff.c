#include <windows.h>
#include <stdint.h>
#include <stdatomic.h>
#include <inttypes.h>
#include <wchar.h>
#include "ringbuff.h"

bool pop(RingBuffer *rb, uint8_t *out, size_t size)
{
    if (rb == nullptr || out == nullptr || size == 0)
    {
        return false;
    }

    uint32_t head = atomic_load(&rb->ring->head);
    uint32_t tail = atomic_load(&rb->ring->tail);
    uint32_t next = (tail + 1) % RING_SIZE;
    if (head == tail)
        return false;

    Slot *slot = &rb->ring->buff[tail];
    uint8_t cpy_size = max(size, slot->size);

    memcpy(out, slot->bytes, cpy_size);

    atomic_store(&rb->ring->tail, next);

#if defined(_WIN32) || defined(_WIN64)
    SetEvent(rb->ev);
#endif

    return true;
}

bool push(RingBuffer *rb, uint8_t *data, size_t size)
{
    if (rb == nullptr || data == nullptr || size == 0)
    {
        return false;
    }

    uint32_t head = atomic_load(&rb->ring->head);
    // Need to wait for acquiring tail lock from shared process, head doesn't matter
    uint32_t tail = atomic_load(&rb->ring->tail);
    uint32_t next = (head + 1) % RING_SIZE;
    if (next == tail || size > SLOT_SIZE)
        return false; // full: caller drops

    Slot *slot = &rb->ring->buff[head];
    slot->size = size;
    memcpy(slot->bytes, data, size);

    atomic_store(&rb->ring->head, next);

#if defined(_WIN32) || defined(_WIN64)
    SetEvent(rb->ev);
#endif

    return true;
}

bool open_ring(RingBuffer *rb)
{
    uint64_t size = sizeof(Ring);
    Ring *ring = nullptr;

#if defined(_WIN32) || defined(_WIN64)
    wchar_t map_name[64];
    wchar_t event_name[64];

    swprintf(map_name, 64, L"Local\\sgvs_video_ring_%u", rb->id);
    swprintf(event_name, 64, L"Local\\sgvs_video_ring_%u_ev", rb->id);

    rb->map = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
                                 (DWORD)(size >> 32), (DWORD)(size & 0xFFFFFFFF),
                                 map_name);
    if (rb->map == nullptr)
    {
        return false;
    }

    // Auto-reset event: a Set before the consumer waits is remembered, so no lost wakeups.
    rb->ev = CreateEventW(nullptr, FALSE, FALSE, event_name);
    if (rb->ev == nullptr)
    {
        CloseHandle(rb->map);
        rb->map = nullptr;
        return false;
    }

    ring = (Ring *)MapViewOfFile(rb->map, FILE_MAP_ALL_ACCESS, 0, 0, size);
#endif

    if (ring == nullptr)
    {
        return false;
    }

    rb->ring = ring;

    return true;
}