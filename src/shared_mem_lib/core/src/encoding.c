#include "shared_mem_lib_core/encoding.h"
#include <windows.h>
#include <bcrypt.h>
#include <stdint.h>

#ifdef _WIN32
uint64_t win_generate_id()
{
    uint64_t out = 0;

    // BCRYPT_USE_SYSTEM_PREFERRED_RNG allows passing NULL for the algorithm handle
    NTSTATUS status = BCryptGenRandom(
        NULL,
        (PUCHAR)&out,
        sizeof(uint64_t),
        BCRYPT_USE_SYSTEM_PREFERRED_RNG);

    if (!BCRYPT_SUCCESS(status))
    {
        return 0;
    }

    return out;
}
#endif

uint64_t generate_id()
{
#ifdef _WIN32
    return win_generate_id();
#endif
    return 0;
}

Encoding new_video_encoding(uint16_t max_bit_rate, uint16_t min_bit_rate, uint16_t frame_height, uint16_t frame_width, uint8_t frame_rate)
{
    Encoding enc;
    enc.id = generate_id();
    enc.data.video_enc.max_bit_rate = max_bit_rate;
    enc.data.video_enc.min_bit_rate = min_bit_rate;
    enc.data.video_enc.frame_height = frame_height;
    enc.data.video_enc.frame_width = frame_width;
    enc.data.video_enc.frame_rate = frame_rate;
    enc.type = Video;
    return enc;
}

Encoding new_audio_encoding(bool is_input)
{
    Encoding enc;
    enc.id = generate_id();
    enc.data.audio_enc.is_input = is_input;
    enc.type = Audio;
    return enc;
}

void set_encoding_outgoing(Encoding *enc)
{
    enc->dir = Outgoing;
}

void set_encoding_incoming(Encoding *enc)
{
    enc->dir = Incoming;
}

bool is_audio_encoding(Encoding enc)
{
    return enc.type == Audio;
}

bool is_video_encoding(Encoding enc)
{
    return enc.type == Video;
}

bool is_incoming_encoding(Encoding enc)
{
    return enc.dir == Incoming;
}

bool is_outgoing_encoding(Encoding enc)
{
    return enc.dir == Outgoing;
}

Encoding nil_encoding(void)
{
    Encoding enc;
    enc.id = 0;
    return enc;
}

bool enc_eq(Encoding a, Encoding b)
{
    return a.id == b.id;
}