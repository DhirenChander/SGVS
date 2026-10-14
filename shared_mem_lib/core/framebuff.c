#include <stdint.h>
#include "framebuff.h"
#include "ring.h"
#include "ringbuff.h"

Encoding nil_encoding()
{
    Encoding enc;
    enc.frame_rate = 0;
    enc.height = 0;
    enc.width = 0;
    return enc;
}

uint64_t encoding_to_int(Encoding enc)
{
    return (enc.frame_rate << 32) | (enc.width << 16) | enc.height;
}

Encoding int_to_encoding(uint64_t val)
{
    Encoding enc;
    enc.frame_rate = (val >> 32) & ((0b1 << 8) - 1);
    enc.width = (val >> 16) & ((0b1 << 16) - 1);
    enc.height = val & ((0b1 << 16) - 1);
    return enc;
}

bool enc_eq(Encoding a, Encoding b)
{
    return a.frame_rate == b.frame_rate && a.height == b.height && a.width == b.width;
}

bool config_has_encoding(Config *conf, Encoding enc)
{
    for (int i = 0; i < MAX_ENCODINGS; i++)
    {
        if (enc_eq(conf->encodings[i], enc))
        {
            return true;
        }
    }

    return false;
}

bool add_ring(FrameBuffer *fb, Encoding enc)
{
    uint64_t id = encoding_to_int(enc);

    fb->ring_count += 1;
    fb->rings = (RingBuffer *)realloc(fb->rings, fb->ring_count * sizeof(RingBuffer));

    if (fb->rings == nullptr)
    {
        printf("Unable to allocate new ring for encoding: %lu", id);
        return false;
    }

    fb->rings[fb->ring_count - 1].id = id;
    return open_ring(&fb->rings[fb->ring_count - 1]);
}

bool request_encoding(FrameBuffer *fb, Encoding enc)
{
    if (config_has_encoding(&fb->config, enc))
    {
        return true;
    }

    Encoding nil = nil_encoding();

    for (int i = 0; i < MAX_ENCODINGS; i++)
    {
        if (enc_eq(fb->config.encodings[i], nil))
        {
            fb->config.encodings[i] = enc;
#if defined(_WIN32) || defined(_WIN64)
            SetEvent(fb->conf_ev);
#endif
            add_ring(fb, enc);

            return true;
        }
    }

    return false;
}

bool read_encodings(FrameBuffer *fb, Encoding *out, uint8_t len)
{
    int len = max(len, MAX_ENCODINGS);
    Encoding nil = nil_encoding();

    for (int i = 0, j = 0; i < MAX_ENCODINGS && j < len; i++)
    {
        if (!enc_eq(fb->config.encodings[i], nil))
        {
            out[j] = fb->config.encodings[i];
            j++;
        }
    }

    return true;
}

bool remove_encoding(FrameBuffer *fb, Encoding enc)
{
    for (int i = 0; i < MAX_ENCODINGS; i++)
    {
        if (enc_eq(fb->config.encodings[i], enc))
        {
            fb->config.encodings[i] = nil_encoding();
#if defined(_WIN32) || defined(_WIN64)
            SetEvent(fb->conf_ev);
#endif
            return true;
        }
    }

    return true;
}

RingBuffer *ringbuff_by_encoding(FrameBuffer *fb, Encoding enc)
{
    uint64_t id = encoding_to_int(enc);

    for (int i = 0; i < fb->ring_count; i++)
    {
        if (fb->rings[i].id == id)
        {
            return &fb->rings[i];
        }
    }

    return nullptr;
}

bool try_pop_frame(FrameBuffer *fb, Encoding enc, Frame *out)
{
    return blocking_pop_frame(fb, enc, out, 0x0);
}

bool blocking_pop_frame(FrameBuffer *fb, Encoding enc, Frame *out, uint32_t max_timeout)
{
    RingBuffer *rb = ringbuff_by_encoding(fb, enc);

    if (rb == nullptr)
    {
        printf("No ring buffer present for this encoding.");
        return false;
    }

    out->encoding = enc;

    return blocking_pop(rb, out->data, out->size, max_timeout);
}