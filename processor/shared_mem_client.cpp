#include <windows.h>

#include <atomic>
#include <cstdint>
#include <string>
#include <format>
#include <vector>
#include <unistd.h>

#define SLOT_SIZE 500000
#define RING_SIZE 64

struct Slot {
    size_t size;
    uint8_t bytes[500000];
};

// Need a struct for the underlying data to ensure function definitions do not get included in the data mapping
struct Ring {
    Slot buff[RING_SIZE];
    std::atomic<uint32_t> head;
    std::atomic<uint32_t> tail;
};

static std::atomic<uint32_t> nextId {0};

class RingBuffer {
    public:
        RingBuffer() {
            this->id = nextId.fetch_add(1, std::memory_order_relaxed);
        }

        bool push(std::vector<uint8_t> data) {
            uint32_t head = this->ring->head.load(std::memory_order_relaxed);
            // Need to wait for acquiring tail lock from shared process, head doesn't matter
            uint32_t tail = this->ring->tail.load(std::memory_order_acquire);
            if (head == tail || data.size() > SLOT_SIZE) return false;   // full: caller drops

            Slot &slot = this->ring->buff[head];
            slot.size = data.size();
            memcpy(slot.bytes, data.data(), data.size());

            this->ring->head.store(head + 1, std::memory_order_release);
            
            #if defined(_WIN32) || defined(_WIN64)
            SetEvent(this->ev); 
            #endif

            return true;
        }

        bool open_ring() {
            uint64_t size = sizeof(Ring);
            Ring *ring;

            #if defined(_WIN32) || defined(_WIN64)
            this->map = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
                                            (DWORD)(size >> 32), (DWORD)(size & 0xFFFFFFFF),
                                            std::format(L"Local\\sgvs_video_ring_{}", this->id).c_str());
            // Auto-reset event: a Set before the consumer waits is remembered, so no lost wakeups
            this->ev = CreateEventW(nullptr, FALSE, FALSE, std::format(L"Local\\sgvs_video_ring_{}_ev", this->id).c_str());
            ring = (Ring*)MapViewOfFile(map, FILE_MAP_ALL_ACCESS, 0, 0, size);
            #endif

            if (ring == nullptr) {
                return false;
            }

            this->ring = ring;

            return true;
        }
    private:
        uint32_t id;
        Ring *ring;
        HANDLE map, ev;
};