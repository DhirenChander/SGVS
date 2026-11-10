package main

import (
	"shared_mem_lib"
)

func main() {
	var fb shared_mem_lib.FrameBuffer

	shared_mem_lib.NewFrameBuffer(&fb)
	fb.SetShareVideo(true)
}
