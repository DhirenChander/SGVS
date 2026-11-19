#ifndef FRAMEBUFF_H

#if defined(_WIN32) || defined(_WIN64)
#include <windows.h>
#endif
#include <stdint.h>
#ifdef __cplusplus
#include <atomic>
#define atomic_uint_least32_t std::atomic_uint_least32_t
#else
#include <stdatomic.h>
#endif
#include <inttypes.h>
#include <stdbool.h>

#include "frame.h"
#include "ringbuff.h"
#include "encoding.h"
#include "message.h"

#ifdef __cplusplus
extern "C"
{
#pragma message("extern C block ACTIVE in " __FILE__)
#endif

#define FRAMEBUFF_H

#define MAX_ENCODINGS 10
#define MSG_EV_COUNT 3

    // TODO: Add audio input field
    typedef struct
    {
        Encoding encodings[MAX_ENCODINGS];
    } Config;

    typedef struct
    {
        RingBuffer msg_chans[MSG_EV_COUNT][MSG_EV_COUNT];
        RingBuffer *rings;
        Config *config; // Used to hold data about active encodings
#ifdef _WIN32
        // Encs_ev is using for encoding updates, msg_evs is used for any other config updates
        HANDLE encs_ev, conf_map, conf_mu;
        CRITICAL_SECTION cs;
#endif
        uint8_t ring_count;
    } FrameBuffer;

    bool new_framebuffer(FrameBuffer *out);
    void free_framebuffer(FrameBuffer *fb);
    bool push_frames(FrameBuffer *fb, Frame *f, uint32_t frame_count);
    bool try_pop_frames(FrameBuffer *fb, Encoding encoding, Frame *out, uint32_t *out_count, uint32_t max_frame_count);
    bool blocking_pop_frames(FrameBuffer *fb, Encoding encoding, Frame *out, uint32_t *out_count, uint32_t max_frame_count, uint32_t max_timeout);
    bool request_encoding(FrameBuffer *fb, Encoding encoding);
    bool remove_encoding(FrameBuffer *fb, Encoding encoding);
    bool blocking_read_encodings(FrameBuffer *fb, Encoding *out, uint8_t *out_len, uint8_t max_len, uint32_t max_timeout);
    bool align_rings_to_config(FrameBuffer *fb);

    // TODO: Fix signalling on encodings and hide this method
    bool read_encodings(FrameBuffer *fb, Encoding *out, uint8_t *out_len, uint8_t max_len);

    bool push_message(FrameBuffer *fb, Message msg);
    bool blocking_pop_message(FrameBuffer *fb, Module src, Module dst, Message *out_msg);

#ifdef __cplusplus
}
#endif

#endif