// Generated using DeepSeek V4.1 Flash via OpenCode.
// Tests for the shared-memory RingBuffer.
//
// The implementation is compiled directly into this translation unit so the
// test can reach the internal Ring type. The test shows that a ring can be
// created, written to, and then read back through a second mapping opened
// directly on the same in-memory file.
#include "shared_mem_client.cpp"

#include <cstdio>

static int failures = 0;

#define CHECK(cond)                                                     \
    do {                                                                \
        if (!(cond)) {                                                  \
            std::printf("FAILED: %s (line %d)\n", #cond, __LINE__);     \
            ++failures;                                                 \
        }                                                               \
    } while (0)

// Read (consume) one slot. Returns false when the ring is empty.
bool read(Ring *r, std::vector<uint8_t> &out) {
    uint32_t tail = r->tail.load(std::memory_order_relaxed);
    // Acquire pairs with the producer's release store to head, so the slot
    // contents written before the store are visible here.
    uint32_t head = r->head.load(std::memory_order_acquire);
    if (tail == head) return false;   // empty

    Slot &slot = r->buff[tail];
    out.assign(slot.bytes, slot.bytes + slot.size);

    r->tail.store((tail + 1) % RING_SIZE, std::memory_order_release);

    return true;
}

int main() {
    // Make the first ring's id deterministic so the test knows the mapping name.
    nextId.store(0, std::memory_order_relaxed);

    RingBuffer writer;
    CHECK(writer.open_ring());

    std::vector<uint8_t> payload {1, 2, 3, 4, 5};
    CHECK(writer.push(payload));

    // Open a second, independent view directly on the same named in-memory file.
    HANDLE map = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE,
                                  L"Local\\sgvs_video_ring_0");
    CHECK(map != nullptr);

    Ring *reader = nullptr;
    if (map != nullptr) {
        reader = (Ring*)MapViewOfFile(map, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(Ring));
    }
    CHECK(reader != nullptr);

    if (reader != nullptr) {
        std::vector<uint8_t> received;
        CHECK(read(reader, received));
        CHECK(received == payload);

        // Draining an empty ring reports empty rather than reading stale data.
        std::vector<uint8_t> extra;
        CHECK(!read(reader, extra));

        UnmapViewOfFile(reader);
    }

    if (map != nullptr) CloseHandle(map);

    if (failures == 0) {
        std::printf("All RingBuffer tests passed.\n");
        return 0;
    }

    std::printf("%d RingBuffer test(s) failed.\n", failures);
    return 1;
}