// Generated with Claude
package shared_mem_lib
 
/*
#cgo CFLAGS: -I../core/include
#cgo LDFLAGS: -L../core/lib -lshared_mem_client
#include "shared_mem_lib_core/encoding.h"
#include "shared_mem_lib_core/framebuff.h"
*/
import "C"
 
type Encoding C.Encoding
type Config C.Config
type Frame C.Frame
type FrameBuffer C.FrameBuffer
 
func (fb *FrameBuffer) c() *C.FrameBuffer { return (*C.FrameBuffer)(fb) }
 
func NewFrameBuffer(out *FrameBuffer) bool {
	return bool(C.new_framebuffer(out.c()))
}
 
func (fb *FrameBuffer) Free() {
	C.free_framebuffer(fb.c())
}
 
func (fb *FrameBuffer) PushFrames(f []Frame) bool {
	return bool(C.push_frames(fb.c(), (*C.Frame)(&f[0]), C.uint32_t(len(f))))
}
 
func (fb *FrameBuffer) TryPopFrames(enc Encoding, out []Frame) bool {
	return bool(C.try_pop_frames(fb.c(), C.Encoding(enc), (*C.Frame)(&out[0]), C.uint32_t(len(out))))
}
 
func (fb *FrameBuffer) BlockingPopFrames(enc Encoding, out []Frame, maxTimeout uint32) bool {
	return bool(C.blocking_pop_frames(fb.c(), C.Encoding(enc), (*C.Frame)(&out[0]), C.uint32_t(len(out)), C.uint32_t(maxTimeout)))
}
 
func (fb *FrameBuffer) RequestEncoding(enc Encoding) bool {
	return bool(C.request_encoding(fb.c(), C.Encoding(enc)))
}
 
func (fb *FrameBuffer) RemoveEncoding(enc Encoding) bool {
	return bool(C.remove_encoding(fb.c(), C.Encoding(enc)))
}
 
func (fb *FrameBuffer) ReadEncodings(out []Encoding) bool {
	return bool(C.read_encodings(fb.c(), (*C.Encoding)(&out[0]), C.uint8_t(len(out))))
}
 
func FrameSizedByEncoding(enc Encoding) Frame {
	return Frame(C.frame_sized_by_encoding(C.Encoding(enc)))
}
 
func (fb *FrameBuffer) SetShareVideo(v bool) {
	C.set_share_video(fb.c(), C.bool(v))
}
 
func (fb *FrameBuffer) TryReadConfig(out *Config) bool {
	return bool(C.try_read_config(fb.c(), (*C.Config)(out)))
}
 
func (fb *FrameBuffer) BlockingReadConfig(out *Config, maxTimeout uint32) bool {
	return bool(C.blocking_read_config(fb.c(), (*C.Config)(out), C.uint32_t(maxTimeout)))
}
 
func (fb *FrameBuffer) ForceReadConfig(out *Config) {
	C.force_read_config(fb.c(), (*C.Config)(out))
}