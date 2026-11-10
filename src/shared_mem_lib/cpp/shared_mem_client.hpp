// Partially Generated With Claude
#pragma once

#if defined(_WIN32) || defined(_WIN64)
#include <windows.h>
#endif

#include <cstdint>
#include <stdio.h>

#include "shared_mem_lib_core/framebuff.h"

namespace shared_mem_client
{
    using Frame = ::Frame;
    using Encoding = ::Encoding;
    using Config = ::Config;

    Frame frame_sized_by_encoding(Encoding enc)
    {
        return ::frame_sized_by_encoding(enc);
    }

    void set_frame_data(Frame *f, uint8_t *data)
    {
        return ::set_frame_data(f, data);
    }

    size_t get_frame_size(Frame *f)
    {
        return ::get_frame_size(f);
    }

    Encoding new_video_encoding(uint16_t max_bit_rate, uint16_t min_bit_rate, uint16_t frame_height, uint16_t frame_width, uint8_t frame_rate)
    {
        return ::new_video_encoding(max_bit_rate, min_bit_rate, frame_height, frame_width, frame_rate);
    }

    Encoding new_audio_encoding(bool is_input)
    {
        return ::new_audio_encoding(is_input);
    }

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

        bool try_pop_frames(Encoding encoding, Frame *out, uint32_t *out_count, uint32_t max_frame_count)
        {
            return ::try_pop_frames(&fb_, encoding, out, out_count, max_frame_count);
        }

        bool blocking_pop_frames(Encoding encoding, Frame *out, uint32_t *out_count, uint32_t max_frame_count, uint32_t max_timeout)
        {
            return ::blocking_pop_frames(&fb_, encoding, out, out_count, max_frame_count, max_timeout);
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

        void set_share_video(bool v)
        {
            ::set_share_video(&fb_, v);
        }

        bool get_sharing_video()
        {
            return ::get_sharing_video(&fb_);
        }

        void set_front_end_encoding(Encoding enc)
        {
            return ::set_front_end_encoding(&fb_, enc);
        }

        Encoding get_front_end_encoding()
        {
            return ::get_front_end_encoding(&fb_);
        }

    private:
        ::FrameBuffer fb_;
    };
}