// Partially Generated With Claude
#pragma once

#if defined(_WIN32) || defined(_WIN64)
#include <windows.h>
#endif

#include <cstdint>
#include <stdio.h>

#include "../core/framebuff.h"

namespace shared_mem_client
{
    using Frame = ::Frame;
    using Encoding = ::Encoding;
    using Config = ::Config;

    class SharedMemoryFrameBuffer
    {
    public:
        explicit SharedMemoryFrameBuffer()
            : fb_(::FrameBuffer{})
        {
            if (!::new_framebuffer(&fb_))
            {
                fprintf(stderr, "Failed to initialize framebuffer\n");
            }
        }

        ~SharedMemoryFrameBuffer()
        {
            ::free_framebuffer(&fb_);
        }

        SharedMemoryFrameBuffer(const SharedMemoryFrameBuffer &) = delete;
        SharedMemoryFrameBuffer &operator=(const SharedMemoryFrameBuffer &) = delete;

        bool push_frames(Frame *f, uint32_t frame_count)
        {
            return ::push_frames(&fb_, f, frame_count);
        }

        bool try_pop_frames(Encoding encoding, Frame *out, uint32_t max_frame_count)
        {
            return ::try_pop_frames(&fb_, encoding, out, max_frame_count);
        }

        bool blocking_pop_frames(Encoding encoding, Frame *out, uint32_t max_frame_count, uint32_t max_timeout)
        {
            return ::blocking_pop_frames(&fb_, encoding, out, max_frame_count, max_timeout);
        }

        bool request_encoding(Encoding encoding)
        {
            return ::request_encoding(&fb_, encoding);
        }

        bool remove_encoding(Encoding encoding)
        {
            return ::remove_encoding(&fb_, encoding);
        }

        bool read_encodings(Encoding *out, uint8_t len)
        {
            return ::read_encodings(&fb_, out, len);
        }

#if defined(_WIN32) || defined(_WIN64)
        bool poll_encodings(Encoding *out, uint8_t len, DWORD *out_err, uint32_t max_timeout)
        {
            return ::poll_encodings(&fb_, out, len, out_err, max_timeout);
        }
#endif

        static Frame frame_sized_by_encoding(Encoding enc)
        {
            return ::frame_sized_by_encoding(enc);
        }

        void set_share_video(bool v)
        {
            ::set_share_video(&fb_, v);
        }

        bool try_read_config(Config *out)
        {
            return ::try_read_config(&fb_, out);
        }

        bool blocking_read_config(Config *out, uint32_t max_timeout)
        {
            return ::blocking_read_config(&fb_, out, max_timeout);
        }

        void force_read_config(Config *out)
        {
            ::force_read_config(&fb_, out);
        }

    private:
        ::FrameBuffer fb_;
    };
}