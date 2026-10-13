#include <stdint.h>
#include "framebuff.h"
#include "ring.h"

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