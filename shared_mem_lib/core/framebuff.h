#ifndef FRAMEBUFF_H

#include <windows.h>
#include <stdint.h>
#include <stdatomic.h>
#include <inttypes.h>
#include <wchar.h>
#include "ringbuff.h"

#define FRAMEBUFF_H

#define ENC_SIZE 5
#define MAX_ENCODINGS 10

typedef struct
{
    uint16_t height;
    uint16_t width;
    uint8_t frame_rate;
} Encoding;

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

#endif