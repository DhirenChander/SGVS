#ifndef ENCODING_H

#define ENCODING_H

#include <stdint.h>

typedef struct
{
    // Separate values for min and max bit rates to allow for selection of capped VBR (max > min) or CBR (max == min)
    uint64_t id;
    uint16_t max_bit_rate;
    uint16_t min_bit_rate;
    uint16_t frame_height;
    uint16_t frame_width;
    uint8_t frame_rate;
} Encoding;

Encoding nil_encoding();
bool enc_eq(Encoding a, Encoding b);

#endif