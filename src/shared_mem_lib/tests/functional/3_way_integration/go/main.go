package main

import (
	"fmt"
	"shared_mem_lib"
	"time"
)

func mock_processor(f *shared_mem_lib.GCFrame, fb *shared_mem_lib.FrameBuffer) {
	for {
		if !fb.PushFrames([]shared_mem_lib.GCFrame{*f, *f, *f}) {
			fmt.Println("Failed to push 3 frames, waiting for buffer to be popped")
		} else {
			fmt.Println("Pushed 3 frames!")
		}

		time.Sleep(time.Millisecond * 100)
	}

	f.Close()
}

func mock_receiver(enc shared_mem_lib.Encoding, fb *shared_mem_lib.FrameBuffer) {
	for {
		succ, frames := fb.BlockingPopFrames(enc, 120, 2000)
		if succ {
			fmt.Println("Received ", len(frames), "frames!")

			for _, v := range frames {
				v.Close()
			}
		} else {
			fmt.Println("Failed to get frames")
		}
	}
}

func main() {
	fmt.Println("Starting Go Processor")
	var fb shared_mem_lib.FrameBuffer
	shared_mem_lib.NewFrameBuffer(&fb)

	enc := shared_mem_lib.NewVideoEncoding(10000, 10000, 1080, 1920, 60)
	enc.SetIncoming()
	fb.RequestEncoding(enc)
	fb.AlignRingsToConfig()

	f := shared_mem_lib.NewGCFrame(enc)

	for i := 0; i < len(f.Bytes); i++ {
		f.Bytes[i] = 0xC3
	}

	go mock_processor(&f, &fb)

	recs := make(map[shared_mem_lib.Encoding]struct{})

	recs[enc] = struct{}{}

	for {
		encs, succ := fb.ReadEncodings(10)
		if succ {
			fb.AlignRingsToConfig()
			fmt.Println("Got: ", len(encs), " Encodings")
			for _, v := range encs {
				_, ok := recs[v]

				if !ok {
					recs[v] = struct{}{}

					if v.IsOutgoing() {
						go mock_receiver(v, &fb)
					}
				}
			}
		} else {
			fmt.Println("Failed to poll encodings")
		}

		time.Sleep(500 * time.Millisecond)
	}
}
