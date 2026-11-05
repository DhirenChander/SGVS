#include <stdint.h>
#include "framebuff.h"
#include "ring.h"
#include "ringbuff.h"
#include <stdio.h>

int ringbuff_by_encoding(FrameBuffer *fb, Encoding enc);

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

            if (!add_ring(fb, enc))
            {
                // Clean up if ring creation fails
                fb->config.encodings[i] = nil;
                return false;
            }

#if defined(_WIN32) || defined(_WIN64)
            SetEvent(fb->encs_ev); // Only set the event on suc
#endif

            return true;
        }
    }

    return false;
}

bool read_encodings(FrameBuffer *fb, Encoding *out, uint8_t len)
{
    int mlen = max(len, MAX_ENCODINGS);
    Encoding nil = nil_encoding();

    for (int i = 0, j = 0; i < MAX_ENCODINGS && j < mlen; i++)
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
    DWORD waitResult = WaitForSingleObject(fb->encs_ev, (DWORD)max_timeout);

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
            SetEvent(fb->encs_ev);
#endif
            if (!remove_ring(fb, enc))
            {
                // Clean-up if ring removal fails
                fb->config.encodings[i] = enc;
#if defined(_WIN32) || defined(_WIN64)
                SetEvent(fb->encs_ev);
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
    uint64_t id = enc.id;

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

bool push_frame(FrameBuffer *fb, Frame *f) {
    int rb_ind = ringbuff_by_encoding(fb, f->encoding);

    if (rb_ind == -1) {
        printf("No ring buffer present for this encoding.");
        return false;
    }

    return push(&fb->rings[rb_ind], f->data, f->size);
}

bool new_framebuffer(FrameBuffer *out) {
    out->rings = malloc(0);
    out->ring_count = 0;

    uint64_t conf_size = sizeof(Config);
    Config *config;

#ifdef _WIN32
    out->conf_map = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
                                 (DWORD)(conf_size >> 32), (DWORD)(conf_size & 0xFFFFFFFF),
                                 L"Local\\sgvs_conf");
    if (out->conf_map == nullptr)
    {
        return false;
    }

    out->encs_ev = CreateEventW(nullptr, FALSE, FALSE, L"Local\\sgvs_conf_encs_ev");
    if (out->encs_ev == nullptr)
    {
        CloseHandle(out->conf_map);
        out->conf_map = nullptr;
        return false;
    }

    out->conf_ev = CreateEventW(nullptr, FALSE, FALSE, L"Local\\sgvs_conf_ev");
    if (out->conf_ev == nullptr)
    {
        CloseHandle(out->conf_map);
        CloseHandle(out->encs_ev);
        out->encs_ev = nullptr;
        out->conf_map = nullptr;
        return false;
    }

    config = (Config *)MapViewOfFile(out->conf_map, FILE_MAP_ALL_ACCESS, 0, 0, conf_size);
#endif

    if (config == nullptr) {
        return false;
    }

    out->config = *config;
    return true;
}

void free_framebuffer(FrameBuffer *fb) {
    free(fb->rings);
#ifdef _WIN32
    UnmapViewOfFile(&fb->config);
    CloseHandle(fb->conf_map);
    CloseHandle(fb->encs_ev);
    CloseHandle(fb->conf_ev);
#endif
    free(fb);
}