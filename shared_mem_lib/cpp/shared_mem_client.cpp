#include <windows.h>

#include <cstdint>
#include <cstdlib>
#include <vector>

#include "../core/ringbuff.h"

class SharedMemoryRingWriter {
public:
    explicit SharedMemoryRingWriter(uint32_t id = 0)
        : rb_(nullptr), owns_memory_(false) {
        rb_ = static_cast<::RingBuffer*>(std::calloc(1, sizeof(::RingBuffer)));
        if (rb_ != nullptr) {
            rb_->id = id;
            owns_memory_ = true;
        }
    }

    ~SharedMemoryRingWriter() {
        if (rb_ != nullptr && owns_memory_) {
            std::free(rb_);
            rb_ = nullptr;
        }
    }

    SharedMemoryRingWriter(const SharedMemoryRingWriter&) = delete;
    SharedMemoryRingWriter& operator=(const SharedMemoryRingWriter&) = delete;

    bool open_ring() {
        return rb_ != nullptr && ::open_ring(rb_);
    }

    bool push(const std::vector<uint8_t>& data) {
        return rb_ != nullptr && ::push(rb_, const_cast<uint8_t*>(data.data()), data.size());
    }

    ::RingBuffer* native_handle() const {
        return rb_;
    }

private:
    ::RingBuffer* rb_;
    bool owns_memory_;
};