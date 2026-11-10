#ifndef FRAMEBUFF_H

#if defined(_WIN32) || defined(_WIN64)
#include <windows.h>
#endif
#include <stdint.h>
#include <stdatomic.h>
#include <inttypes.h>
#include <stdbool.h>

#include "frame.h"
#include "ringbuff.h"
#include "encoding.h"

#define FRAMEBUFF_H

#define MAX_ENCODINGS 10

// TODO: Add audio input field
typedef struct
{
    Encoding encodings[MAX_ENCODINGS];
    Encoding front_end_video;
    uint32_t video_selector; // PID when isProcess is true, Screen ID when isProcess is false
    bool is_process;
    bool sharing_screen;
} Config;

typedef struct
{
    uint8_t ring_count;
    RingBuffer *rings;
    Config config; // Used to hold data about active encodings
#ifdef _WIN32
    // Encs_ev is using for encoding updates, conf_ev is used for any other config updates
    HANDLE encs_ev, conf_ev, conf_map;
#endif
} FrameBuffer;

bool new_framebuffer(FrameBuffer *out);
void free_framebuffer(FrameBuffer *fb);
bool push_frames(FrameBuffer *fb, Frame *f, uint32_t frame_count);
bool try_pop_frames(FrameBuffer *fb, Encoding encoding, Frame *out, uint32_t max_frame_count);
bool blocking_pop_frames(FrameBuffer *fb, Encoding encoding, Frame *out, uint32_t max_frame_count, uint32_t max_timeout);
bool request_encoding(FrameBuffer *fb, Encoding encoding);
bool remove_encoding(FrameBuffer *fb, Encoding encoding);
bool read_encodings(FrameBuffer *fb, Encoding *out, uint8_t len);
#if defined(_WIN32) || defined(_WIN64)
bool poll_encodings(FrameBuffer *fb, Encoding *out, uint8_t len, DWORD *out_err, uint32_t max_timeout);
#endif

// Config methods
void set_share_video(FrameBuffer *fb, bool v);
bool get_sharing_video(FrameBuffer *fb);
void set_front_end_encoding(FrameBuffer *fb, Encoding enc);
Encoding get_front_end_encoding(FrameBuffer *fb);

#endif