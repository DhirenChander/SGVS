#if defined(_WIN32) || defined(_WIN64)
#include <windows.h>
#endif
#include <stdio.h>
#include <string.h>
#include <stdint.h>
#include <stdatomic.h>
#include <inttypes.h>
#include "shared_mem_lib_core/ringbuff.h"

#if defined(_WIN32) || defined(_WIN64)
void shm_name_suffix(wchar_t out[18])
{
    out[0] = L'\0';
    wchar_t value[18];
    DWORD n = GetEnvironmentVariableW(L"SGVS_SHM_INSTANCE", value, 18);
    // unset or empty: no suffix, the normal case
    if (n == 0) return;

    // too long, or anything but letters and digits (a backslash would change which object the name points at).
    // falling back to the shared names silently would mix this instance's traffic with another's, so say so once
    bool valid = n < 17;
    for (DWORD i = 0; valid && i < n; i++)
    {
        wchar_t c = value[i];
        valid = (c >= L'0' && c <= L'9') || (c >= L'a' && c <= L'z') || (c >= L'A' && c <= L'Z');
    }
    if (!valid)
    {
        static volatile LONG warned = 0;
        if (InterlockedExchange(&warned, 1) == 0)
            fprintf(stderr, "SGVS_SHM_INSTANCE ignored: use 1-16 letters or digits. Using the default shared memory names.\n");
        return;
    }
    out[0] = L'_';
    memcpy(out + 1, value, (n + 1) * sizeof(wchar_t));
}
#endif

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
    // printf("Popping %d...\n", max_slots_out);

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

    uint32_t available = available_in_buffer(rb);
    *out_count = min(available, max_slots_out);
    uint32_t next = (tail + *out_count) % rb->ring->slot_count;

    for (uint32_t i = 0; i < *out_count; i++)
    {
        size_t len = rb->lens[(tail + i) % rb->ring->slot_count];
        // fprintf(stderr, "Copying %d, %zu...\n", i, len);
        // Deep copy each slot
        uint8_t *slot = get_slot(rb, (tail + i) % rb->ring->slot_count);
        memcpy(out[i].bytes, slot, len);
        out[i].len = len;
        // fprintf(stderr, "Size: %zu...\n", out[i].len);
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

    uint32_t free = ((uint64_t)tail + (uint64_t)rb->ring->slot_count - (uint64_t)head - 1) % rb->ring->slot_count;

    if (size > free)
        return false; // full: caller drops

    uint32_t next = (head + size) % rb->ring->slot_count;

    for (uint32_t i = 0; i < size; i++)
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
    wchar_t map_name[128];
    wchar_t event_name[128];
    char id_str[80];
    write_uint256(id_str, rb->id);

    wchar_t suffix[18];
    shm_name_suffix(suffix);
    swprintf(map_name, 128, L"Local\\sgvs_ring_%hs%ls", id_str, suffix);
    swprintf(event_name, 128, L"Local\\sgvs_ring_%hs%ls_ev", id_str, suffix);

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
}

uint32_t available_in_buffer(RingBuffer *rb)
{
    uint32_t head = atomic_load(&rb->ring->head);
    uint32_t tail = atomic_load(&rb->ring->tail);

    return ((uint64_t)head + (uint64_t)rb->ring->slot_count - (uint64_t)tail) % rb->ring->slot_count;
}