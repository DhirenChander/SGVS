// Generated with Claude
package shared_mem_lib

/*
#cgo CFLAGS: -I../core/include
#cgo LDFLAGS: -L./lib -lshared_mem_client -lcrypto
#include "shared_mem_lib_core/encoding.h"
#include "shared_mem_lib_core/framebuff.h"
*/
import "C"
import (
	"bytes"
	"encoding/binary"
	"fmt"
	"io"
	"reflect"
	"runtime"
)

type Encoding C.Encoding
type Config C.Config
type Frame C.Frame
type FrameBuffer C.FrameBuffer
type Message C.Message

/*
START_SHARING,
STOP_SHARING,
HOST_CODE,
ANSWER_CODE,
START_HOST,
JOIN_CALL,
PARTICIPANT_JOINED,
PARTICIPANT_LEFT,
LEAVE_CALL,
*/
type Operation int

const (
	StopSharing = iota
	HostCode
	AnswerCode
	StartHost
	JoinCall
	ParticipantJoined
	ParticipantLeft
	LeaveCall
)

type Module int

const (
	Processor = iota
	Network
	Application
)

type GCFrame struct {
	Raw    *Frame
	Bytes  []byte
	pinner runtime.Pinner
}

func (fb *FrameBuffer) c() *C.FrameBuffer { return (*C.FrameBuffer)(fb) }

func NewFrameBuffer(out *FrameBuffer) bool {
	return bool(C.new_framebuffer(out.c()))
}

func (fb *FrameBuffer) Free() {
	C.free_framebuffer(fb.c())
}

// bool align_rings_to_config(FrameBuffer *fb);
func (fb *FrameBuffer) AlignRingsToConfig() bool {
	return bool(C.align_rings_to_config((*C.FrameBuffer)(fb)))
}

func (fb *FrameBuffer) PushFrames(fs []GCFrame) bool {
	inRaw := make([]Frame, len(fs))

	for i := 0; i < len(fs); i++ {
		inRaw[i] = *fs[i].Raw
	}

	return bool(C.push_frames(fb.c(), (*C.Frame)(&inRaw[0]), C.uint32_t(len(fs))))
}

func (fb *FrameBuffer) TryPopFrames(enc Encoding, out []Frame, outCount *uint32) bool {
	return bool(C.try_pop_frames(fb.c(), C.Encoding(enc), (*C.Frame)(&out[0]), (*C.uint32_t)(outCount), C.uint32_t(len(out))))
}

// BlockingPopFrames reallocates each frame and the buffer as a whole, to minimize space
func (fb *FrameBuffer) BlockingPopFrames(enc Encoding, maxFrames uint32, maxTimeout uint32) (bool, []GCFrame) {
	var outLen uint32
	out := make([]GCFrame, maxFrames)
	rawOut := make([]Frame, maxFrames)

	for i := range out {
		out[i] = NewGCFrame(enc)
		rawOut[i] = *out[i].Raw
	}

	ok := bool(C.blocking_pop_frames(fb.c(), C.Encoding(enc), (*C.Frame)(&rawOut[0]),
		(*C.uint32_t)(&outLen), C.uint32_t(maxFrames), C.uint32_t(maxTimeout)))

	n := int(outLen)
	if !ok || n > len(out) {
		for i := range out {
			out[i].pinner.Unpin()
		}
		return false, nil
	}

	for i := 0; i < n; i++ {
		size := int(rawOut[i].GetFrameSize())
		if size > cap(out[i].Bytes) {
			size = cap(out[i].Bytes)
		}
		*out[i].Raw = rawOut[i]
		out[i].Bytes = out[i].Bytes[:size]
	}

	for i := n; i < len(out); i++ {
		out[i].pinner.Unpin()
	}

	return true, out[:n]
}

// bool blocking_read_encodings(FrameBuffer *fb, Encoding *out, uint8_t *out_len, uint8_t max_len, uint32_t max_timeout);
func (fb *FrameBuffer) BlockingReadEncodings(maxLen uint8, maxTimeout uint32) ([]Encoding, bool) {
	encs := make([]Encoding, maxLen)
	var outLen uint8

	if !bool(C.blocking_read_encodings(fb.c(), (*C.Encoding)(&encs[0]), (*C.uint8_t)(&outLen), C.uint8_t(maxLen), C.uint32_t(maxTimeout))) {
		return nil, false
	}

	return encs[:outLen], true
}

func (fb *FrameBuffer) RequestEncoding(enc Encoding) bool {
	return bool(C.request_encoding(fb.c(), C.Encoding(enc)))
}

func (fb *FrameBuffer) RemoveEncoding(enc Encoding) bool {
	return bool(C.remove_encoding(fb.c(), C.Encoding(enc)))
}

// bool read_encodings(FrameBuffer *fb, Encoding *out, uint8_t *out_len, uint8_t max_len);
func (fb *FrameBuffer) ReadEncodings(maxLen uint8) ([]Encoding, bool) {
	encs := make([]Encoding, maxLen)
	var outLen uint8

	if !bool(C.read_encodings(fb.c(), (*C.Encoding)(&encs[0]), (*C.uint8_t)(&outLen), C.uint8_t(maxLen))) {
		return nil, false
	}

	return encs[:outLen], true
}

// bool push_message(FrameBuffer *fb, Message msg);
func (fb *FrameBuffer) PushMessage(msg Message) bool {
	return bool(C.push_message(fb.c(), C.Message(msg)))
}

// bool blocking_pop_message(FrameBuffer *fb, Module src, Module dst, Message *out_msg);
func (fb *FrameBuffer) BlockingPopMessage(src, dst Module) (Message, bool) {
	var out Message

	res := bool(C.blocking_pop_message(fb.c(), C.Module(src), C.Module(dst), (*C.Message)(&out)))

	return out, res
}

func FrameSizedByEncoding(enc Encoding) (Frame, uint64) {
	outSize := uint64(0)
	f := Frame(C.frame_sized_by_encoding(C.Encoding(enc), (*C.size_t)(&outSize)))
	return f, outSize
}

func (f *Frame) SetFrameData(buff []byte) {
	C.set_frame_data((*C.Frame)(f), (*C.uint8_t)(&buff[0]))
}

func (f *Frame) GetFrameSize() uint64 {
	return uint64(C.get_frame_size((*C.Frame)(f)))
}

func NewGCFrame(enc Encoding) GCFrame {
	g := GCFrame{}

	f, size := FrameSizedByEncoding(enc)
	g.Raw = &f

	g.Bytes = make([]byte, size)
	g.pinner.Pin(&g.Bytes[0])
	f.SetFrameData(g.Bytes)

	return g
}

func (g *GCFrame) Close() {
	g.pinner.Unpin()
}

// Encoding new_video_encoding(uint16_t max_bit_rate, uint16_t min_bit_rate, uint16_t frame_height, uint16_t frame_width, uint8_t frame_rate);
func NewVideoEncoding(maxBitRate, minBitRate, frameHeight, frameWidth uint16, frameRate uint8) Encoding {
	return Encoding(C.new_video_encoding(C.uint16_t(maxBitRate), C.uint16_t(minBitRate), C.uint16_t(frameHeight), C.uint16_t(frameWidth), C.uint8_t(frameRate)))
}

// Encoding new_audio_encoding(bool is_input);
func NewAudioEncoding(isInput bool) Encoding {
	return Encoding(C.new_audio_encoding(C.bool(isInput)))
}

func (e *Encoding) SetOutgoing() {
	C.set_encoding_outgoing((*C.Encoding)(e))
}

func (e *Encoding) SetIncoming() {
	C.set_encoding_incoming((*C.Encoding)(e))
}

func (e Encoding) IsAudio() bool {
	return bool(C.is_audio_encoding(C.Encoding(e)))
}

func (e Encoding) IsVideo() bool {
	return bool(C.is_video_encoding(C.Encoding(e)))
}

func (e Encoding) IsIncoming() bool {
	return bool(C.is_incoming_encoding(C.Encoding(e)))
}

func (e Encoding) IsOutgoing() bool {
	return bool(C.is_outgoing_encoding(C.Encoding(e)))
}

func EncodingEq(a, b Encoding) bool {
	return bool(C.enc_eq(C.Encoding(a), C.Encoding(b)))
}

func (msg *Message) GetOperation() Operation {
	return Operation(msg.opcode)
}

func NewMessage(src, dst Module, opcode Operation, args ...interface{}) (Message, error) {
	var msg Message
	msg.src = C.Module(src)
	msg.dest = C.Module(dst)
	msg.opcode = C.Operation(opcode)

	// Generated with claude
	var buf bytes.Buffer
	for n, a := range args {
		if err := binary.Write(&buf, binary.NativeEndian, a); err != nil { // Go 1.21+
			return Message{}, fmt.Errorf("argument %d (%T): %w", n, a, err)
		}
	}
	if buf.Len() > len(msg.data) {
		return Message{}, fmt.Errorf("arguments are %d bytes, max is %d", buf.Len(), len(msg.data))
	}
	for i, b := range buf.Bytes() {
		msg.data[i] = C.uchar(b) // adjust to msg.data's element type
	}
	// msg.len = C.uint(buf.Len()) // if the C struct has a length field

	return msg, nil
}

func NewMessageStr(src, dst Module, opcode Operation, data string) (Message, error) {
	if len(data) > 255 {
		return Message{}, fmt.Errorf("String is too long for message, max length is 255")
	}

	var msg Message
	msg.src = C.Module(src)
	msg.dest = C.Module(dst)
	msg.opcode = C.Operation(opcode)

	var buf bytes.Buffer

	buf.WriteString(data)

	for i, b := range buf.Bytes() {
		msg.data[i] = C.uchar(b)
	}

	msg.data[255] = 0

	return msg, nil
}

// Generated with claude, returns an array of any values matching the passed arg types
func (msg *Message) UnmarshallMessage(argTypes ...reflect.Type) ([]any, error) {
	// Copy the cgo array into a Go byte slice (adjust if msg.data is already []byte).
	raw := make([]byte, len(msg.data))
	for i, b := range msg.data {
		raw[i] = byte(b)
	}
	// If the C struct has a length field, trim it here: raw = raw[:msg.len]

	r := bytes.NewReader(raw)
	out := make([]any, 0, len(argTypes))

	for n, t := range argTypes {
		if t == nil {
			return nil, fmt.Errorf("type %d is nil", n)
		}

		if t.Kind() == reflect.String {
			var length uint16
			if err := binary.Read(r, binary.NativeEndian, &length); err != nil {
				return nil, fmt.Errorf("argument %d (%v): reading string length: %w", n, t, err)
			}
			buf := make([]byte, length)
			if _, err := io.ReadFull(r, buf); err != nil {
				return nil, fmt.Errorf("argument %d (%v): reading string data: %w", n, t, err)
			}
			// Convert so named string types (type Foo string) round-trip correctly.
			out = append(out, reflect.ValueOf(string(buf)).Convert(t).Interface())
			continue
		}

		ptr := reflect.New(t)
		if err := binary.Read(r, binary.NativeEndian, ptr.Interface()); err != nil {
			return nil, fmt.Errorf("argument %d (%v): %w", n, t, err)
		}
		out = append(out, ptr.Elem().Interface())
	}

	return out, nil
}
