#if defined(_WIN32) || defined(_WIN64)
#include <windows.h>
#endif

#include <cstdint>
#include <stdio.h>

#include "../core/framebuff.h"

namespace shared_mem_client {
    using Frame = ::Frame;
    using Encoding = ::Encoding;

    class SharedMemoryFrameBuffer
    {
    public:
        explicit SharedMemoryFrameBuffer()
            : fb_(::FrameBuffer{})
        {
            if(!::new_framebuffer(&fb_)) {
                fprintf(stderr, "Failed to initialize framebuffer\n");
            }
        }

        ~SharedMemoryFrameBuffer()
        {
            ::free_framebuffer(&fb_);
        }

        SharedMemoryFrameBuffer(const SharedMemoryFrameBuffer &) = delete;
        SharedMemoryFrameBuffer &operator=(const SharedMemoryFrameBuffer &) = delete;

        bool push_frame(Frame *f) {
            return ::push_frame(&fb_, f);
        }

        bool try_pop_frame(Encoding encoding, Frame *out) {
            return ::try_pop_frame(&fb_, encoding, out);
        }

        bool blocking_pop_frame(::Encoding encoding, ::Frame *out, uint32_t max_timeout) {
            return ::blocking_pop_frame(&fb_, encoding, out, max_timeout);
        }

        bool request_encoding(Encoding encoding) {
            return ::request_encoding(&fb_, encoding);
        }

        bool remove_encoding(Encoding encoding) {
            return ::remove_encoding(&fb_, encoding);
        }

        bool read_encodings(Encoding *out, uint8_t len) {
            return ::read_encodings(&fb_, out, len);
        }

    #ifdef _WIN32
        bool poll_encodings(FrameBuffer *fb, Encoding *out, uint8_t len, DWORD *out_err, uint32_t max_timeout) {
            return ::poll_encodings(&fb_, out, len, out_err, max_timeout);
        }
    #endif

    private:
        ::FrameBuffer fb_;
    };
}