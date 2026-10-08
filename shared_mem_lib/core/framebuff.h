#ifndef FRAMEBUFF_H

#include <windows.h>
#include <stdint.h>
#include <stdatomic.h>
#include <inttypes.h>
#include <wchar.h>
#include "ringbuff.h"
 
#define FRAMEBUFF_H

typedef struct {
    RingBuffer **rings;
    RingBuffer config; // Used to hold data about active encodings
} FrameBuffer;

typedef struct {
    uint8_t encoding;
    uint8_t size;
    uint8_t *data;
} Frame;

bool push_frame(FrameBuffer *fb, Frame *f);
bool pop_frame(FrameBuffer *fb, uint8_t encoding, uint8_t* out, size_t size);
bool request_encoding(FrameBuffer *fb, uint8_t encoding);
bool remove_encoding(FrameBuffer *fb, uint8_t encoding);
bool read_encodings(FrameBuffer *fb, uint8_t* out, size_t size);

#endif