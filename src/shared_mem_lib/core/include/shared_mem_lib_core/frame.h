#ifndef FRAME_H

#include <stdint.h>
#include "encoding.h"

#ifdef __cplusplus
extern "C"
{
#endif

    typedef struct
    {
        Encoding encoding;
        size_t size;
        uint8_t *data;
    } Frame;

    Frame frame_sized_by_encoding(Encoding enc, size_t *out_size);
    void set_frame_data(Frame *f, uint8_t *data);
    size_t get_frame_size(Frame *f);

#ifdef __cplusplus
}
#endif

#endif