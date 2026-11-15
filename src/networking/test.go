package main

import (
	"encoding/hex"
	"fmt"
	"shared_mem_lib"
	"time"
)

// mock for frame buffer
type mockFrameBuffer struct {
	pushedFrames []shared_mem_lib.GCFrame      //Tracks pushed frames but is not read from
	framesToPop  chan []shared_mem_lib.GCFrame //push frames here to simulate receiving them from processing module
	totalPushed  int
	totalPopped  int
}

func runTests() bool {
	return testAudioTransfer()
}

//TODO: Check if we can test with a real frame buffer instead of mocking

func testAudioTransfer() bool {

	//Need one frame buffer for each direction?
	var fbMockHost *mockFrameBuffer = &mockFrameBuffer{framesToPop: make(chan []shared_mem_lib.GCFrame, 100)}
	var fbMockOther *mockFrameBuffer = &mockFrameBuffer{framesToPop: make(chan []shared_mem_lib.GCFrame, 100)}

	// connectMocksFbs(fbMockHost, fbMockOther)

	//Set up so that they reference each other?

	hostPc, _, _, _,
		hostSelfVoiceTrack, _, hostRemoteVoiceTrackChan, _,
		err := generateOffer()

	if err != nil {
		fmt.Println(err.Error())
		return false
	}

	otherPc, _, _, _,
		otherSelfVoiceTrack, _, otherRemoteVoiceTrackChan, _,
		err := generateAnswer(getSDPStr(hostPc))

	if err != nil {
		fmt.Println(err.Error())
		return false
	}

	err = applyAnswer(hostPc, getSDPStr(otherPc))
	if err != nil {
		fmt.Println(err.Error())
		return false
	}

	goAndPrintIfError(func() error {
		return relayAudioOnceConnected(hostSelfVoiceTrack, hostRemoteVoiceTrackChan, fbMockHost)
	})

	goAndPrintIfError(func() error {
		return relayAudioOnceConnected(otherSelfVoiceTrack, otherRemoteVoiceTrackChan, fbMockOther)
	})

	var dummyPayload [63]byte
	for i := 0; i < len(dummyPayload); i++ {
		dummyPayload[i] = byte(42)
	}

	enc := shared_mem_lib.NewAudioEncoding(true)
	enc.SetOutgoing()

	//Wait until connected?
	// <-time.After(2 * time.Second)

	sender := fbMockHost
	receiver := fbMockOther

	numFrameSets := 10
	for range numFrameSets {
		dummyFrame1 := shared_mem_lib.NewGCFrame(enc)
		dummyFrame1.Bytes = dummyPayload[:]

		dummyFrame2 := shared_mem_lib.NewGCFrame(enc)
		dummyFrame2.Bytes = dummyPayload[:]

		fmt.Println("Pushing Dummy Frame Set")
		sender.AppendFrames([]shared_mem_lib.GCFrame{dummyFrame1, dummyFrame2})
	}

	//verify that other side recieves them
	<-time.After(1 * time.Second)

	fmt.Printf("Total received frames: %d\n", receiver.totalPushed)

	for i, v := range receiver.pushedFrames {
		fmt.Printf("Frame %d: %s\n", i, bytesToHexStr(v.Bytes))
	}

	if receiver.totalPushed == numFrameSets*2 {
		return true
	}

	//Keep PCs alive?
	return false
}

func bytesToHexStr(bytes []byte) string {
	return hex.EncodeToString(bytes)
}

func (mfb *mockFrameBuffer) PushFrames(frames []shared_mem_lib.GCFrame) bool {
	// if enc.IsOutgoing() {
	// 	return false
	// }
	mfb.pushedFrames = append(mfb.pushedFrames, frames...)
	mfb.totalPushed += len(frames)

	return true
}

func (mfb *mockFrameBuffer) RequestEncoding(enc shared_mem_lib.Encoding) bool {
	return true
}

func (mfb *mockFrameBuffer) AlignRingsToConfig() bool {
	return true
}

func (mfb *mockFrameBuffer) BlockingPopFrames(
	enc shared_mem_lib.Encoding,
	_ uint32,
	_ uint32,
) (bool, []shared_mem_lib.GCFrame) {
	if enc.IsIncoming() {
		return false, []shared_mem_lib.GCFrame{}
	}

	outFrames := <-mfb.framesToPop //Wait for next frames to arrive
	mfb.totalPopped += len(outFrames)
	return true, outFrames
}

func (mfb *mockFrameBuffer) AppendFrames(frames []shared_mem_lib.GCFrame) {
	mfb.framesToPop <- frames
}

func (fb *mockFrameBuffer) ReadEncodings(maxLen uint8) ([]shared_mem_lib.Encoding, bool) {
	return []shared_mem_lib.Encoding{}, false
}
