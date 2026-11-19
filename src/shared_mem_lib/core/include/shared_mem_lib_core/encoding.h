#ifndef ENCODING_H

#define ENCODING_H

#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C"
{
#endif

    typedef struct
    {
        // Separate values for min and max bit rates to allow for selection of capped VBR (max > min) or CBR (max == min)
        uint16_t max_bit_rate;
        uint16_t min_bit_rate;
        uint16_t frame_height;
        uint16_t frame_width;
        uint8_t frame_rate;
    } VideoEncoding;

    typedef struct
    {
        bool is_input;
    } AudioEncoding;

    typedef enum
    {
        Video,
        Audio
    } EncodingType;

    typedef enum
    {
        Incoming,
        Outgoing,
    } EncodingDir;

    typedef struct
    {
        union
        {
            VideoEncoding video_enc;
            AudioEncoding audio_enc;
        } data;

        uint64_t id;
        EncodingType type;
        EncodingDir dir;
    } Encoding;

    Encoding nil_encoding(void);
    Encoding new_video_encoding(uint16_t max_bit_rate, uint16_t min_bit_rate, uint16_t frame_height, uint16_t frame_width, uint8_t frame_rate);
    Encoding new_audio_encoding(bool is_input);
    void set_encoding_outgoing(Encoding *enc);
    void set_encoding_incoming(Encoding *enc);
    bool is_audio_encoding(Encoding enc);
    bool is_video_encoding(Encoding enc);
    bool is_incoming_encoding(Encoding enc);
    bool is_outgoing_encoding(Encoding enc);
    bool enc_eq(Encoding a, Encoding b);

#ifdef __cplusplus
}
#endif

#endif