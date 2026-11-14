// Partially Generated With Claude
#pragma once

#if defined(_WIN32) || defined(_WIN64)
#include <windows.h>
#endif

#include <cstdint>
#include <stdio.h>

#include "../core/include/shared_mem_lib_core/framebuff.h"

namespace shared_mem_client
{
    using Frame = ::Frame;
    using Encoding = ::Encoding;
    using Config = ::Config;

    Frame frame_sized_by_encoding(Encoding enc, size_t *out_size)
    {
        return ::frame_sized_by_encoding(enc, out_size);
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

    void set_encoding_outgoing(Encoding *enc)
    {
        ::set_encoding_outgoing(enc);
    }

    void set_encoding_incoming(Encoding *enc)
    {
        ::set_encoding_incoming(enc);
    }

    bool is_audio_encoding(Encoding enc)
    {
        return ::is_audio_encoding(enc);
    }

    bool is_video_encoding(Encoding enc)
    {
        return ::is_video_encoding(enc);
    }

    bool is_incoming_encoding(Encoding enc)
    {
        return ::is_incoming_encoding(enc);
    }

    bool is_outgoing_encoding(Encoding enc)
    {
        return ::is_outgoing_encoding(enc);
    }

    bool enc_eq(Encoding a, Encoding b)
    {
        return ::enc_eq(a, b);
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

        // blocking_read_encodings(FrameBuffer *fb, Encoding *out, uint8_t *out_len, uint8_t max_len, uint32_t max_timeout)
        bool blocking_read_encodings(Encoding *out, uint8_t *out_len, uint8_t max_len, uint32_t max_timeout)
        {
            return ::blocking_read_encodings(&fb_, out, out_len, max_len, max_timeout);
        }

        void set_share_video(bool v)
        {
            ::set_share_video(&fb_, v);
        }

        bool get_sharing_video()
        {
            return ::get_sharing_video(&fb_);
        }

        bool align_rings_to_config()
        {
            return ::align_rings_to_config(&fb_);
        }

    private:
        ::FrameBuffer fb_;
    };
}