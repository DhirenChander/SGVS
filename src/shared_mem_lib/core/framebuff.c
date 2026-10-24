#include <stdint.h>
#include "framebuff.h"
#include "ring.h"
#include "ringbuff.h"

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
    fb->ring_count += 1;
    fb->rings = (RingBuffer *)realloc(fb->rings, fb->ring_count * sizeof(RingBuffer));

    if (fb->rings == nullptr)
    {
        printf("Unable to allocate new ring for encoding: %lu", enc.id);
        return false;
    }

    fb->rings[fb->ring_count - 1].id = enc.id;
    return open_ring(&fb->rings[fb->ring_count - 1]);
}

bool remove_ring(FrameBuffer *fb, Encoding enc)
{
    int rb_ind = ringbuff_by_encoding(fb, enc);

    if (rb_ind == -1)
    {
        return true;
    }

    free_ringbuffer(&fb->rings[rb_ind]);
    memcpy(&fb->rings[rb_ind], &fb->rings[rb_ind + 1], (fb->ring_count - rb_ind - 1) * sizeof(RingBuffer));
    fb->ring_count -= 1;
    fb->rings = (RingBuffer *)realloc(fb->rings, fb->ring_count * sizeof(RingBuffer));

    return true;
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

            if (!add_ring(fb, enc))
            {
                // Clean up if ring creation fails
                fb->config.encodings[i] = nil;
#if defined(_WIN32) || defined(_WIN64)
                SetEvent(fb->conf_ev);
#endif
                return false;
            }

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

#if defined(_WIN32) || defined(_WIN64)
bool poll_encodings(FrameBuffer *fb, Encoding *out, uint8_t len, DWORD *out_err, uint32_t max_timeout)
{
    DWORD waitResult = WaitForSingleObject(fb->conf_ev, (DWORD)max_timeout);

    switch (waitResult)
    {
    case WAIT_OBJECT_0:
        break;

    case WAIT_TIMEOUT:
        printf("No updates within the timeout, returning.\n");
        break;

    case WAIT_FAILED:
        printf("WaitForSingleObject failed with error (%lu)\n", GetLastError());
        break;

    default:
        printf("Unexpected wait result: %lu\n", waitResult);
        break;
    }

    if (out_err != nullptr)
    {
        *out_err = waitResult;
    }

    if (waitResult != WAIT_OBJECT_0)
    {
        return false;
    }

    return read_encodings(fb, out, len);
}
#endif

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
            if (!remove_ring(fb, enc))
            {
                // Clean-up if ring removal fails
                fb->config.encodings[i] = enc;
#if defined(_WIN32) || defined(_WIN64)
                SetEvent(fb->conf_ev);
#endif
                return false;
            }

            return true;
        }
    }

    return true;
}

int ringbuff_by_encoding(FrameBuffer *fb, Encoding enc)
{
    uint64_t id = encoding_to_int(enc);

    for (int i = 0; i < fb->ring_count; i++)
    {
        if (fb->rings[i].id == id)
        {
            return i;
        }
    }

    return -1;
}

bool try_pop_frame(FrameBuffer *fb, Encoding enc, Frame *out)
{
    return blocking_pop_frame(fb, enc, out, 0x0);
}

bool blocking_pop_frame(FrameBuffer *fb, Encoding enc, Frame *out, uint32_t max_timeout)
{
    int rb_ind = ringbuff_by_encoding(fb, enc);

    if (rb_ind == -1)
    {
        printf("No ring buffer present for this encoding.");
        return false;
    }

    out->encoding = enc;

    return blocking_pop(&fb->rings[rb_ind], out->data, out->size, max_timeout);
}