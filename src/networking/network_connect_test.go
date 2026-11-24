package main

import (
	"bytes"
	"encoding/hex"
	"fmt"
	"shared_mem_lib"
	"sync"
	"testing"
	"time"

	"github.com/pion/webrtc/v4"
)

// mock for frame buffer
type mockFrameBuffer struct {
	mu           sync.Mutex
	pushedFrames []shared_mem_lib.GCFrame      //Tracks pushed frames but is not read from
	framesToPop  chan []shared_mem_lib.GCFrame //push frames here to simulate receiving them from processing module
	encodings    []shared_mem_lib.Encoding     //what ReadEncodings reports, simulates processing registering encodings
	videoToPop   chan []shared_mem_lib.GCFrame //video access units waiting to be "read from shared memory"
	pushedBytes  [][]byte                      //a copy of every pushed frame's bytes
	totalPushed  int
	totalPopped  int
}

func (mfb *mockFrameBuffer) pushed() int {
	mfb.mu.Lock()
	defer mfb.mu.Unlock()
	return mfb.totalPushed
}

// pion drops samples written before ICE and DTLS finish, so tests must wait for this before pushing
func waitConnected(t *testing.T, pcs ...*webrtc.PeerConnection) {
	t.Helper()
	deadline := time.Now().Add(10 * time.Second)
	for _, pc := range pcs {
		for pc.ConnectionState() != webrtc.PeerConnectionStateConnected {
			if time.Now().After(deadline) {
				t.Fatalf("peer connection did not connect, state: %s", pc.ConnectionState())
			}
			time.Sleep(20 * time.Millisecond)
		}
	}
}

// TODO: Check if we can test with a real frame buffer instead of mocking
func TestAudioTransfer(t *testing.T) {

	//Need one frame buffer for each direction?
	var fbMockHost *mockFrameBuffer = &mockFrameBuffer{framesToPop: make(chan []shared_mem_lib.GCFrame, 100)}
	var fbMockOther *mockFrameBuffer = &mockFrameBuffer{framesToPop: make(chan []shared_mem_lib.GCFrame, 100)}

	enc := shared_mem_lib.NewAudioEncoding(true)
	enc.SetOutgoing()
	fbMockHost.encodings = []shared_mem_lib.Encoding{enc}

	hostPc, _, _, _, hostMedia, err := generateOffer()

	if err != nil {
		fmt.Println(err.Error())
		t.FailNow()
	}

	otherPc, _, _, _, otherMedia, err := generateAnswer(getSDPStr(hostPc))

	if err != nil {
		fmt.Println(err.Error())
		t.FailNow()
	}

	err = applyAnswer(hostPc, getSDPStr(otherPc))
	if err != nil {
		fmt.Println(err.Error())
		t.FailNow()
	}

	goAndPrintIfError(func() error {
		return relayAudioOnceConnected(hostMedia.VoiceTrack, hostMedia.RemoteVoice, fbMockHost)
	})

	goAndPrintIfError(func() error {
		return relayAudioOnceConnected(otherMedia.VoiceTrack, otherMedia.RemoteVoice, fbMockOther)
	})

	var dummyPayload [63]byte
	for i := 0; i < len(dummyPayload); i++ {
		dummyPayload[i] = byte(42)
	}

	waitConnected(t, hostPc, otherPc)

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
		// the jitter buffer holds at most 8 packets, so a burst could never all arrive
		time.Sleep(20 * time.Millisecond)
	}

	//verify that other side recieves them
	<-time.After(1 * time.Second)

	fmt.Printf("Total received frames: %d\n", receiver.pushed())

	receiver.mu.Lock()
	for i, v := range receiver.pushedFrames {
		fmt.Printf("Frame %d: %s\n", i, bytesToHexStr(v.Bytes))
	}
	receiver.mu.Unlock()

	if receiver.pushed() < numFrameSets*2*8/10 {
		t.Fatalf("expected at least %d frames, got %d", numFrameSets*2*8/10, receiver.pushed())
	}
}

// an access unit the way the frontend's encoder makes it: AUD, SPS, PPS, then a keyframe slice big enough to need FU-A
func fakeAccessUnit(fill byte) []byte {
	au := []byte{0, 0, 0, 1, 9, 0xF0}
	au = append(au, 0, 0, 0, 1, 0x67, 0x42, 0xC0, 0x1F, 0xAA, 0xBB)
	au = append(au, 0, 0, 0, 1, 0x68, 0xCE, 0x3C, 0x80)
	au = append(au, 0, 0, 0, 1, 0x65, 0x88, 0x84)
	return append(au, bytes.Repeat([]byte{fill}, 3000)...)
}

func TestVideoTransfer(t *testing.T) {
	fbMockHost := &mockFrameBuffer{
		framesToPop: make(chan []shared_mem_lib.GCFrame, 100),
		videoToPop:  make(chan []shared_mem_lib.GCFrame, 100),
	}
	fbMockOther := &mockFrameBuffer{
		framesToPop: make(chan []shared_mem_lib.GCFrame, 100),
		videoToPop:  make(chan []shared_mem_lib.GCFrame, 100),
	}

	videoEnc := shared_mem_lib.NewVideoEncoding(videoBitrateKbps, videoBitrateKbps, 720, 1280, 30)
	videoEnc.SetOutgoing()
	fbMockHost.encodings = []shared_mem_lib.Encoding{videoEnc}

	hostPc, _, _, _, hostMedia, err := generateOffer()
	if err != nil {
		t.Fatal(err)
	}
	otherPc, _, _, _, otherMedia, err := generateAnswer(getSDPStr(hostPc))
	if err != nil {
		t.Fatal(err)
	}
	if err := applyAnswer(hostPc, getSDPStr(otherPc)); err != nil {
		t.Fatal(err)
	}

	relayMediaOnceConnected(hostMedia, fbMockHost)
	relayMediaOnceConnected(otherMedia, fbMockOther)

	waitConnected(t, hostPc, otherPc)

	const sent = 10
	for i := 0; i < sent; i++ {
		// no zero bytes in the fill, so it can't look like a start code
		frame := shared_mem_lib.NewGCFrameWithData(videoEnc, fakeAccessUnit(byte(i+10)))
		fbMockHost.AppendVideo([]shared_mem_lib.GCFrame{frame})
		time.Sleep(33 * time.Millisecond)
	}

	deadline := time.Now().Add(3 * time.Second)
	for fbMockOther.pushed() < sent*8/10 && time.Now().Before(deadline) {
		time.Sleep(20 * time.Millisecond)
	}

	fbMockOther.mu.Lock()
	defer fbMockOther.mu.Unlock()
	if len(fbMockOther.pushedBytes) < sent*8/10 {
		t.Fatalf("expected at least %d access units, got %d", sent*8/10, len(fbMockOther.pushedBytes))
	}

	idrHeader := []byte{0, 0, 0, 1, 0x65, 0x88, 0x84}
	fills := map[byte]bool{}
	for i, au := range fbMockOther.pushedBytes {
		if !bytes.HasPrefix(au, []byte{0, 0, 0, 1, 9, 0xF0}) {
			t.Fatalf("access unit %d does not start with an AUD: %x", i, au[:min(len(au), 12)])
		}
		at := bytes.Index(au, idrHeader)
		if at < 0 || len(au) < at+len(idrHeader)+3000 {
			t.Fatalf("access unit %d lost its keyframe slice (len %d)", i, len(au))
		}
		slice := au[at+len(idrHeader) : at+len(idrHeader)+3000]
		if !bytes.Equal(slice, bytes.Repeat(slice[:1], 3000)) {
			t.Fatalf("access unit %d slice is corrupted", i)
		}
		fills[slice[0]] = true
	}
	if len(fills) < sent*8/10 {
		t.Fatalf("access units were repeated or merged: %d distinct of %d", len(fills), sent)
	}
}

func bytesToHexStr(bytes []byte) string {
	return hex.EncodeToString(bytes)
}

func (mfb *mockFrameBuffer) PushFrames(frames []shared_mem_lib.GCFrame) bool {
	mfb.mu.Lock()
	defer mfb.mu.Unlock()
	mfb.pushedFrames = append(mfb.pushedFrames, frames...)
	for _, f := range frames {
		mfb.pushedBytes = append(mfb.pushedBytes, bytes.Clone(f.Bytes))
	}
	mfb.totalPushed += len(frames)

	return true
}

func (mfb *mockFrameBuffer) RequestEncoding(enc shared_mem_lib.Encoding) bool {
	return true
}

func (mfb *mockFrameBuffer) AlignRingsToConfig() bool {
	return true
}

func (mfb *mockFrameBuffer) PushMessage(msg *shared_mem_lib.Message) bool {
	return true
}

// nothing is ever waiting in the mock's message queue
func (mfb *mockFrameBuffer) BlockingPopMessage(src, dst shared_mem_lib.Module, maxTimeout uint32) (shared_mem_lib.Message, bool) {
	time.Sleep(time.Duration(min(maxTimeout, 50)) * time.Millisecond)
	return shared_mem_lib.Message{}, false
}

func (mfb *mockFrameBuffer) BlockingPopFrames(
	enc shared_mem_lib.Encoding,
	_ uint32,
	timeout uint32,
) (bool, []shared_mem_lib.GCFrame) {
	if enc.IsIncoming() {
		return false, []shared_mem_lib.GCFrame{}
	}

	if enc.IsVideo() {
		select {
		case frames := <-mfb.videoToPop:
			return true, frames
		case <-time.After(time.Duration(timeout) * time.Millisecond):
			return false, nil
		}
	}

	outFrames := <-mfb.framesToPop //Wait for next frames to arrive
	mfb.mu.Lock()
	mfb.totalPopped += len(outFrames)
	mfb.mu.Unlock()
	return true, outFrames
}

func (mfb *mockFrameBuffer) AppendVideo(frames []shared_mem_lib.GCFrame) {
	mfb.videoToPop <- frames
}

func (mfb *mockFrameBuffer) AppendFrames(frames []shared_mem_lib.GCFrame) {
	mfb.framesToPop <- frames
}

func (fb *mockFrameBuffer) ReadEncodings(maxLen uint8) ([]shared_mem_lib.Encoding, bool) {
	return fb.encodings, true
}

// func testCallAndResponse(host bool, fb *shared_mem_lib.FrameBuffer) *webrtc.PeerConnection {
// 	if host /* We are the host */ {

// 		peerConnection, dataChan, dataChanOpen, messages, err := setupPcAsHost(fb)

// 		if err != nil {
// 			fmt.Printf("Error setting up connection: %s\n", err)
// 			return nil
// 		}

// 		//Wait for data channel to open
// 		fmt.Println("Waiting for datachannel to open")
// 		<-dataChanOpen

// 		fmt.Println("Sending message from host")
// 		dataChan.SendText("Hello from host :)")

// 		//Wait for response
// 		fmt.Println("Waiting for response")
// 		response := <-messages

// 		if !response.IsString {
// 			fmt.Printf("Message was not a string")
// 			return nil
// 		}

// 		fmt.Printf("Recieved Message: %s\n", string(response.Data))

// 		return peerConnection

// 	} else /* We are not the host */ {

// 		peerConnection, dcReady, dataChanOpen, messages, err := setupPcAsAttendee(fb)

// 		if err != nil {
// 			fmt.Printf("Error setting up connection: %s\n", err)
// 			return nil
// 		}

// 		fmt.Println("Waiting for datachannel to be created")
// 		dataChan := <-dcReady

// 		fmt.Println("Waiting for datachannel to open")
// 		<-dataChanOpen

// 		fmt.Println("Waiting for message from host")
// 		firstMessage := <-messages

// 		if !firstMessage.IsString {
// 			fmt.Printf("Message was not a string")
// 			return nil
// 		}

// 		fmt.Printf("Recieved Message: %s\n", string(firstMessage.Data))

// 		fmt.Println("Sending reponse")

// 		dataChan.SendText("Hello from the other side")

// 		return peerConnection
// 	}

// }
