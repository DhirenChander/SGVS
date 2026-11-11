#if defined(_WIN32) || defined(_WIN64)
#include <windows.h>
#endif
#include <stdio.h>
#include <stdint.h>
#include <stdatomic.h>
#include <inttypes.h>
#include "shared_mem_lib_core/ringbuff.h"

uint8_t *get_slot(RingBuffer *rb, uint32_t ind)
{
    return &rb->data[ind * rb->ring->slot_size];
}

bool try_pop(RingBuffer *rb, Slot *out, uint32_t *out_count, uint32_t max_slots_out)
{
    return blocking_pop(rb, out, out_count, max_slots_out, 0x0);
}

bool blocking_pop(RingBuffer *rb, Slot *out, uint32_t *out_count, uint32_t max_slots_out, uint32_t max_timeout)
{
    printf("Popping %d...\n", max_slots_out);

    if (rb == nullptr || out == nullptr || max_slots_out == 0)
    {
        return false;
    }

    uint32_t head = atomic_load(&rb->ring->head);
    uint32_t tail = atomic_load(&rb->ring->tail);

    // Block on the event
    if (head == tail)
    {
        if (max_timeout == 0)
        {
            printf("Buffer was empty, could not complete immediate pop.\n");
            return false;
        }

#ifdef _WIN32
        DWORD waitResult = WaitForSingleObject(rb->ev, (DWORD)max_timeout);

        switch (waitResult)
        {
        case WAIT_OBJECT_0:
            break;

        case WAIT_TIMEOUT:
            printf("The wait operation timed out, buffer was not filled in the requested timeout.\n");
            return false;

        case WAIT_FAILED:
            printf("WaitForSingleObject failed with error (%lu)\n", GetLastError());
            return false;

        default:
            printf("Unexpected wait result: %lu\n", waitResult);
            return false;
        }
#endif

        head = atomic_load(&rb->ring->head);
        tail = atomic_load(&rb->ring->tail);

        // Shouldn't happen in a spsc scenario, but still need to check
        // Can occur in an unsupported OS
        if (head == tail)
        {
            printf("Buffer was empty after timeout.\n");
            return false;
        }
    }

    uint32_t available = (head - tail) % rb->ring->slot_count;
    *out_count = min(available, max_slots_out);
    uint32_t next = (tail + *out_count) % rb->ring->slot_count;

    for (int i = 0; i < *out_count; i++)
    {
        size_t len = rb->lens[(tail + i) % rb->ring->slot_count];
        fprintf(stderr, "Copying %d, %d...\n", i, len);
        // Deep copy each slot
        uint8_t *slot = get_slot(rb, (tail + i) % rb->ring->slot_count);
        memcpy(out[i].bytes, slot, len);
        out[i].len = len;
        fprintf(stderr, "Size: %d...\n", out[i].len);
    }

    atomic_store(&rb->ring->tail, next);

    // Re-raise the signal if there are items left
    if (available > *out_count)
    {
#ifdef _WIN32
        SetEvent(rb->ev);
#endif
    }

    return true;
}

bool push(RingBuffer *rb, Slot *data, uint32_t size)
{
    if (rb == nullptr || data == nullptr || size == 0)
    {
        return false;
    }

    uint32_t head = atomic_load(&rb->ring->head);
    // Need to wait for acquiring tail lock from shared process, head doesn't matter
    uint32_t tail = atomic_load(&rb->ring->tail);

    if (size > (tail - (head + 1)) % rb->ring->slot_count)
        return false; // full: caller drops

    uint32_t next = (head + size) % rb->ring->slot_count;

    for (int i = 0; i < size; i++)
    {
        if (data[i].len > rb->ring->slot_size)
            return false;

        uint8_t *slot = get_slot(rb, (head + i) % rb->ring->slot_count);
        memcpy(slot, data[i].bytes, data[i].len);
        rb->lens[(head + i) % rb->ring->slot_count] = data[i].len;
        // fprintf(stderr, "Slot Size: %d...\n", rb->lens[(head + i) % rb->ring->slot_count]);
    }

    atomic_store(&rb->ring->head, next);

#if defined(_WIN32) || defined(_WIN64)
    SetEvent(rb->ev);
#endif

    return true;
}

bool open_ring(RingBuffer *rb, uint64_t slot_size, uint32_t slot_count)
{
    uint64_t size = sizeof(Ring) + (sizeof(size_t) + slot_size) * slot_count;
    Ring *ring = nullptr;

#if defined(_WIN32) || defined(_WIN64)
    wchar_t map_name[64];
    wchar_t event_name[64];

    swprintf(map_name, 64, L"Local\\sgvs_ring_%llu", rb->id);
    swprintf(event_name, 64, L"Local\\sgvs_ring_%llu_ev", rb->id);

    rb->map = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
                                 (DWORD)(size >> 32), (DWORD)(size & 0xFFFFFFFF),
                                 map_name);
    if (rb->map == nullptr)
    {
        return false;
    }

    DWORD err = GetLastError();

    // Auto-reset event: a Set before the consumer waits is remembered, so no lost wakeups.
    rb->ev = CreateEventW(nullptr, FALSE, FALSE, event_name);
    if (rb->ev == nullptr)
    {
        CloseHandle(rb->map);
        rb->map = nullptr;
        return false;
    }

    // Shared memory layout:
    // Ring | Lens[0] | Lens[1] | ... | Lens[n] | Data[0] | Data[1] | ... | Data[n]
    uint8_t *raw_map = (uint8_t *)MapViewOfFile(rb->map, FILE_MAP_ALL_ACCESS, 0, 0, size);

    if (raw_map == nullptr)
    {
        CloseHandle(rb->map);
        CloseHandle(rb->ev);
        rb->ev = nullptr;
        rb->map = nullptr;
        return false;
    }

    ring = (Ring *)raw_map;

    // Initial map value, map is not initialized
    if (err == 0)
    {
        memset(raw_map, 0, size);
        ring->slot_size = slot_size;
        ring->slot_count = slot_count;
        atomic_store(&ring->head, 0);
        atomic_store(&ring->tail, 0);
    }

    rb->lens = (size_t *)(raw_map + sizeof(Ring));
    rb->data = (uint8_t *)(raw_map + sizeof(Ring) + sizeof(size_t) * slot_count);
#endif

    if (ring == nullptr)
    {
        return false;
    }

    rb->ring = ring;

    return true;
}

void free_ringbuffer(RingBuffer *rb)
{
#if defined(_WIN32) || defined(_WIN64)
    UnmapViewOfFile(rb->ring);
    CloseHandle(rb->map);
    CloseHandle(rb->ev);
#endif

    free(rb);
}