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

typedef struct
{
    Encoding encodings[MAX_ENCODINGS];
} Config;

typedef struct
{
    uint8_t ring_count;
    RingBuffer *rings;
    Config config; // Used to hold data about active encodings
    HANDLE conf_ev, conf_map;
} FrameBuffer;

typedef struct
{
    Encoding encoding;
    uint8_t size;
    uint8_t *data;
} Frame;

bool push_frame(FrameBuffer *fb, Frame *f);
bool try_pop_frame(FrameBuffer *fb, Encoding encoding, Frame *out);
bool blocking_pop_frame(FrameBuffer *fb, Encoding encoding, Frame *out, uint32_t max_timeout);
bool request_encoding(FrameBuffer *fb, Encoding encoding);
bool remove_encoding(FrameBuffer *fb, Encoding encoding);
bool read_encodings(FrameBuffer *fb, Encoding *out, uint8_t len);
#if defined(_WIN32) || defined(_WIN64)
bool poll_encodings(FrameBuffer *fb, Encoding *out, uint8_t len, DWORD *out_err, uint32_t max_timeout);
#endif

#endif