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
	messages     chan shared_mem_lib.Message //messages "sent by the frontend", nil means none ever arrive
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
	for i := range len(dummyPayload) {
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
	<-time.After(500 * time.Millisecond)

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
	// closing ends the relays too (they stop once the connection is closed), so nothing outlives the test
	defer hostPc.Close()
	otherPc, _, _, _, otherMedia, err := generateAnswer(getSDPStr(hostPc))
	if err != nil {
		t.Fatal(err)
	}
	defer otherPc.Close()
	if err := applyAnswer(hostPc, getSDPStr(otherPc)); err != nil {
		t.Fatal(err)
	}

	relayMediaOnceConnected(hostMedia, fbMockHost)
	relayMediaOnceConnected(otherMedia, fbMockOther)

	waitConnected(t, hostPc, otherPc)

	const sent = 10
	for i := range sent {
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

func (mfb *mockFrameBuffer) PushFrames(fs []shared_mem_lib.GCFrame) bool {
	mfb.mu.Lock()
	defer mfb.mu.Unlock()
	mfb.pushedFrames = append(mfb.pushedFrames, fs...)
	for _, f := range fs {
		mfb.pushedBytes = append(mfb.pushedBytes, bytes.Clone(f.Bytes))
	}
	mfb.totalPushed += len(fs)

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

// pops what the test put in messages. with no messages channel nothing is ever waiting
func (mfb *mockFrameBuffer) BlockingPopMessage(src, dst shared_mem_lib.Module, max_timeout uint32) (shared_mem_lib.Message, bool) {
	timeout := time.Duration(min(max_timeout, 50)) * time.Millisecond
	if mfb.messages == nil {
		time.Sleep(timeout)
		return shared_mem_lib.Message{}, false
	}
	select {
	case msg := <-mfb.messages:
		return msg, true
	case <-time.After(timeout):
		return shared_mem_lib.Message{}, false
	}
}

func (mfb *mockFrameBuffer) BlockingPopFrames(
	enc shared_mem_lib.Encoding,
	maxFrames uint32,
	maxTimeout uint32,
) (bool, []shared_mem_lib.GCFrame) {
	if enc.IsIncoming() {
		return false, []shared_mem_lib.GCFrame{}
	}

	if enc.IsVideo() {
		select {
		case frames := <-mfb.videoToPop:
			return true, frames
		case <-time.After(time.Duration(maxTimeout) * time.Millisecond):
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
