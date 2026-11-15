package main

import "shared_mem_lib"

func testAudioTransfer() {

	// var fbMock frameBuffer = &mockFrameBuffer{
	// pushResult: true,
	// popResult:  true,
	// }
	//Research how to instantiate mock

	hostPc, _, _, _,
		hostSelfVoiceTrack, _, hostRemoteVoiceTrackChan, _, //Use receivers later to judge network conditions
		err := generateOffer()

	otherPc, _, _, _,
		otherSelfVoiceTrack, _, otherRemoteVoiceTrackChan, _, //Use receivers later to judge network conditions
		err := generateAnswer(getSDPStr(hostPc))

	err = applyAnswer(hostPc, getSDPStr(otherPc))

	go relayAudioOnceConnected(hostSelfVoiceTrack, hostRemoteVoiceTrackChan, fb)
	go relayAudioOnceConnected(otherSelfVoiceTrack, otherRemoteVoiceTrackChan, fb)

}

// Create mocks for frame buffer
type mockFrameBuffer struct {
	// pushedFrames []shared_mem_lib.GCFrame
	// framesToPop  []shared_mem_lib.GCFrame
}

func (m *mockFrameBuffer) PushFrames(frames []shared_mem_lib.GCFrame) bool {
	// m.pushedFrames = append(m.pushedFrames, frames...)
	// return m.pushResult
}

func (m *mockFrameBuffer) BlockingPopFrames(
	enc shared_mem_lib.Encoding,
	maxFrames uint32,
	maxTimeout uint32,
) (bool, []shared_mem_lib.GCFrame) {
	// return m.popResult, m.framesToPop
}
