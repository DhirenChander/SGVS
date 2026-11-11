#include "shared_mem_lib_core/frame.h"

Frame frame_sized_by_encoding(Encoding enc, size_t *out_size)
{
    Frame f = {0};
    f.encoding = enc;
    f.size = (enc.type == Audio) ? 1275 : 500000;
    *out_size = f.size;
    return f;
}

void set_frame_data(Frame *f, uint8_t *data)
{
    f->data = data;
}

size_t get_frame_size(Frame *f)
{
    return f->size;
}