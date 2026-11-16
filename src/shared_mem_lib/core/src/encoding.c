#include "shared_mem_lib_core/encoding.h"
#include <windows.h>
#include <bcrypt.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>
#include <inttypes.h>

uint256_t zero()
{
    return (uint256_t){.limbs = (uint64_t[4]){0, 0, 0, 0}};
}

bool uint256_eq(uint256_t a, uint256_t b)
{
    return a.limbs[0] == b.limbs[0] && a.limbs[1] == b.limbs[1] && a.limbs[2] == b.limbs[2] && a.limbs[3] == b.limbs[3];
}

int write_uint256(char *buff, uint256_t val)
{
    return sprintf(buff, PRIu64 PRIu64 PRIu64 PRIu64, val.limbs[0], val.limbs[1], val.limbs[2], val.limbs[3]);
}

#ifdef _WIN32
uint256_t win_generate_id()
{
    uint256_t out = zero();

    // BCRYPT_USE_SYSTEM_PREFERRED_RNG allows passing NULL for the algorithm handle
    NTSTATUS status = BCryptGenRandom(
        NULL,
        (PUCHAR)&out.limbs,
        sizeof(uint64_t) * 4,
        BCRYPT_USE_SYSTEM_PREFERRED_RNG);

    if (!BCRYPT_SUCCESS(status))
    {
        return zero();
    }

    return out;
}
#endif

uint256_t generate_id()
{

#ifdef _WIN32
    return win_generate_id();
#endif
    return zero();
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
    enc.id = zero();
    return enc;
}

bool enc_eq(Encoding a, Encoding b)
{
    return uint256_eq(a.id, b.id);
}