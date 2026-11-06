#ifndef FRAMEBUFF_H

#if defined(_WIN32) || defined(_WIN64)
#include <windows.h>
#endif
#include <stdint.h>
#include <stdatomic.h>
#include <inttypes.h>
#include <wchar.h>
#include "ringbuff.h"
#include "encoding.h"

#define FRAMEBUFF_H

#define MAX_ENCODINGS 10

// TODO: Add audio input field
typedef struct
{
    Encoding encodings[MAX_ENCODINGS];
    uint32_t video_selector; // PID when isProcess is true, Screen is with isProcess is false
    bool isProcess;
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

typedef struct
{
    Encoding encoding;
    uint8_t size;
    uint8_t *data;
} Frame;

bool new_framebuffer(FrameBuffer *out);
void free_framebuffer(FrameBuffer *fb);
bool push_frame(FrameBuffer *fb, Frame *f);
bool try_pop_frame(FrameBuffer *fb, Encoding encoding, Frame *out);
bool blocking_pop_frame(FrameBuffer *fb, Encoding encoding, Frame *out, uint32_t max_timeout);
bool request_encoding(FrameBuffer *fb, Encoding encoding);
bool remove_encoding(FrameBuffer *fb, Encoding encoding);
bool read_encodings(FrameBuffer *fb, Encoding *out, uint8_t len);
#if defined(_WIN32) || defined(_WIN64)
bool poll_encodings(FrameBuffer *fb, Encoding *out, uint8_t len, DWORD *out_err, uint32_t max_timeout);
#endif
Frame frame_sized_by_encoding(Encoding enc);

#endif