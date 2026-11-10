// Generated with Claude
package shared_mem_lib

/*
#cgo CFLAGS: -I../core/include
#cgo LDFLAGS: -L../core/lib -lshared_mem_client -lcrypto
#include "shared_mem_lib_core/encoding.h"
#include "shared_mem_lib_core/framebuff.h"
*/
import "C"
import "unsafe"

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

func (fb *FrameBuffer) SetShareVideo(v bool) {
	C.set_share_video(fb.c(), C.bool(v))
}

// bool get_sharing_video(FrameBuffer *fb);
func (fb *FrameBuffer) GetSharingVideo() bool {
	return bool(C.get_sharing_video(fb.c()))
}

// void set_front_end_encoding(FrameBuffer *fb, Encoding enc);
func (fb *FrameBuffer) SetFrontEndEncoding(enc Encoding) {
	C.set_front_end_encoding(fb.c(), C.Encoding(enc))
}

// Encoding get_front_end_encoding(FrameBuffer *fb);
func (fb *FrameBuffer) GetFrontEndEncoding() Encoding {
	return Encoding(C.get_front_end_encoding(fb.c()))
}

func FrameSizedByEncoding(enc Encoding) Frame {
	return Frame(C.frame_sized_by_encoding(C.Encoding(enc)))
}

func (f *Frame) SetFrameData(buff []byte) {
	C.set_frame_data((*C.Frame)(f), (*C.uint8_t)(unsafe.Pointer(&buff[0])))
}

func (f *Frame) GetFrameSize() uint64 {
	return uint64(C.get_frame_size((*C.Frame)(f)))
}

func NewFrame(enc Encoding) Frame {
	f := FrameSizedByEncoding(enc)
	buff := make([]byte, f.GetFrameSize())
	f.SetFrameData(buff)
	return f
}

//Encoding new_video_encoding(uint16_t max_bit_rate, uint16_t min_bit_rate, uint16_t frame_height, uint16_t frame_width, uint8_t frame_rate);
func NewVideoEncoding(maxBitRate, minBitRate, frameHeight, frameWidth uint16, frameRate uint8) Encoding {
	return Encoding(C.new_video_encoding(C.uint16_t(maxBitRate), C.uint16_t(minBitRate), C.uint16_t(frameHeight), C.uint16_t(frameWidth), C.uint8_t(frameRate)))
}

//Encoding new_audio_encoding(bool is_input);
func NewAudioEncoding(isInput bool) Encoding {
	return Encoding(C.new_audio_encoding(C.bool(isInput)))
}
