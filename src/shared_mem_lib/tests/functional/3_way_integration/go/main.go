package main

import (
	"shared_mem_lib"
)

func main() {
	var fb shared_mem_lib.FrameBuffer
	shared_mem_lib.NewFrameBuffer(&fb)

	enc := shared_mem_lib.NewVideoEncoding(10000, 10000, 1080, 1920, 60)
	fb.RequestEncoding(enc)

	f := shared_mem_lib.NewGCFrame(enc)

	for i := 0; i < len(f.Bytes); i++ {
		f.Bytes[i] = 0x10
	}

	fb.PushFrames([]shared_mem_lib.Frame{f.Raw})
}
