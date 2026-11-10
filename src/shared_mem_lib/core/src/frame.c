#include "shared_mem_lib_core/frame.h"

Frame frame_sized_by_encoding(Encoding enc)
{
    Frame f;
    f.encoding = enc;
    f.size = (enc.type == Audio) ? 16 : 500000;
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