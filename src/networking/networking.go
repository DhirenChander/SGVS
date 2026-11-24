package main

import (
	"errors"
	"fmt"
	"io"
	"os"
	"strings"
	"sync"
	"time"

	"shared_mem_lib"

	"github.com/pion/interceptor/pkg/jitterbuffer"
	"github.com/pion/rtp/codecs"
	"github.com/pion/webrtc/v4/pkg/media"
	"github.com/pion/webrtc/v4/pkg/media/samplebuilder"

	"github.com/pion/webrtc/v4"
)

const managementChannelName = "transceiver_management"
const voiceAudioChannelName = "voice_audio"

// The screen stream. Its track must be in the first offer/answer: connection strings are swapped by hand once
// and there is no renegotiation afterwards.
const screenVideoTrackID = "screen_video"
const videoFrameDuration = time.Second / 30
const videoBitrateKbps = 3000                                                                    // matches VideoFormat.BitrateKbps in the frontend
const maxVideoFrameBytes = 500000                                                                // one video slot in shared memory, bigger access units are dropped
const screenVideoFmtp = "level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f" // pion's default H.264 line

// audBytes is put in front of every received access unit. The frontend's decoder only emits a frame once it
// sees the next delimiter, so this makes each one come out at once instead of one frame late.
var audBytes = []byte{0, 0, 0, 1, 9, 0xF0}

var StunServers = []string{
	"stun:stun.l.google.com:19302",
	// "stun:stun1.l.google.com:19302",
	// "stun:stun2.l.google.com:19302",
	"stun:stun3.l.google.com:19302",
	// "stun:stun4.l.google.com:19302",
}

var ICEConfiguration = webrtc.Configuration{
	ICEServers: []webrtc.ICEServer{{
		URLs: StunServers,
	}},
}

// Where connection logging goes. Stdout normally; stderr in ipc mode so stdout stays a clean protocol stream.
var logOut io.Writer = os.Stdout

// Webrtc API with adjusted timeouts
var peerAPI = func() *webrtc.API {
	settings := webrtc.SettingEngine{}
	settings.SetICETimeouts(
		1*time.Minute, // disconnected timeout
		5*time.Minute, // failed timeout
		5*time.Second, // keep-alive interval
	)

	return webrtc.NewAPI(webrtc.WithSettingEngine(settings))
}()

// Everything media-related that belongs to one peer connection
type peerMedia struct {
	VoiceTrack  *webrtc.TrackLocalStaticSample // our voice
	VideoTrack  *webrtc.TrackLocalStaticSample // our screen stream
	RemoteVoice chan *webrtc.TrackRemote       // their voice, once it starts arriving
	RemoteVideo chan *webrtc.TrackRemote       // their screen stream, once it starts arriving
	Closed      chan struct{}                  // closed when the connection is closed or failed

	closeOnce sync.Once
}

func newPeerMedia() *peerMedia {
	return &peerMedia{
		RemoteVoice: make(chan *webrtc.TrackRemote, 1),
		RemoteVideo: make(chan *webrtc.TrackRemote, 1),
		Closed:      make(chan struct{}),
	}
}

func (m *peerMedia) markClosed() {
	m.closeOnce.Do(func() { close(m.Closed) })
}

// Adds our tracks. For the host this goes before the offer is created, for the guest after the host's offer is applied.
func (m *peerMedia) addTracks(pc *webrtc.PeerConnection) error {
	var err error
	if m.VoiceTrack, _, err = AddVoiceTrack(pc); err != nil {
		return err
	}
	m.VideoTrack, err = AddScreenVideoTrack(pc)
	return err
}

// When the remote adds a track, hand it to whoever relays that kind of media
func (m *peerMedia) onTrack(remoteTrack *webrtc.TrackRemote, _ *webrtc.RTPReceiver) {
	switch remoteTrack.ID() {
	case voiceAudioChannelName:
		m.RemoteVoice <- remoteTrack
	case screenVideoTrackID:
		m.RemoteVideo <- remoteTrack
	}
}

// Setting up connections
func generateOffer() (
	*webrtc.PeerConnection,
	*webrtc.DataChannel,
	chan bool, //Signals when dataChannel is open
	chan webrtc.DataChannelMessage, //Messages are sent to here (should this be an input instead of output?)
	*peerMedia,
	error) {

	// create a peer connection
	pc, err := peerAPI.NewPeerConnection(ICEConfiguration)
	if err != nil {
		return nil, nil, nil, nil, nil, err
	}
	pm := newPeerMedia()
	logPeerConnectionState(pc, "host", pm)

	keepConnection := false
	defer func() {
		if !keepConnection {
			//If setup fails we may get an error on close, but we can ignore it
			_ = pc.Close()
		}
	}()

	//Start with initial data channel for sending application messages
	//This will let us add appropriate transcievers after the connection is established
	dataChannel, err := pc.CreateDataChannel(managementChannelName, nil)
	if err != nil {
		return nil, nil, nil, nil, nil, err
	}

	if err := pm.addTracks(pc); err != nil {
		return nil, nil, nil, nil, nil, err
	}
	pc.OnTrack(pm.onTrack)

	dataChanOpen := make(chan bool, 1)
	dcMessages := make(chan webrtc.DataChannelMessage, 1)

	dataChannel.OnMessage(func(msg webrtc.DataChannelMessage) {
		dcMessages <- msg
	})

	dataChannel.OnOpen(func() {
		dataChanOpen <- true
		close(dataChanOpen)
	})

	// create a first offer that does not contain any local candidates
	offer, err := pc.CreateOffer(nil)
	if err != nil {
		return nil, nil, nil, nil, nil, err
	}

	// gatherComplete is a channel that will be closed when
	// the gathering of local candidates is complete.
	gatherComplete := webrtc.GatheringCompletePromise(pc)

	// apply the offer
	err = pc.SetLocalDescription(offer)
	if err != nil {
		return nil, nil, nil, nil, nil, err
	}

	// wait for gathering of local candidates to complete
	// Add timeout?
	<-gatherComplete

	// compute the local offer again
	offer2 := pc.LocalDescription()

	// This second offer contains all candidates (no trickling), and can be sent to peer

	// Check that it contains at least one candidate
	// Potentially move this check outside of this function if we need to differentiate it or act differently
	if !strings.Contains(offer2.SDP, "\na=candidate:") {
		return nil, nil, nil, nil, nil, fmt.Errorf("No SDP candidates found, offer is not usable")
	}

	keepConnection = true
	return pc, dataChannel, dataChanOpen, dcMessages, pm, nil
}

func generateAnswer(offerSDP string) (
	*webrtc.PeerConnection,
	chan *webrtc.DataChannel, //Return a channel that will recieve the DataChannel once it is created
	//                          Because channel is only received once host finished setup, we cannot wait for channel here

	chan bool, //Signals when dataChannel is open
	chan webrtc.DataChannelMessage, //Messages are sent to here (should this be an input instead of output?)
	*peerMedia,
	error) {

	pc, err := peerAPI.NewPeerConnection(ICEConfiguration)
	if err != nil {
		return nil, nil, nil, nil, nil, err
	}
	pm := newPeerMedia()
	logPeerConnectionState(pc, "attendee", pm)

	// Close the connection if setup fails
	// Calling this as a deferred function lets us avoid calling it before every return
	keepConnection := false
	defer func() {
		if !keepConnection {
			//If setup fails we may get an error on close, but we can ignore it
			_ = pc.Close()
		}
	}()

	remoteOffer := webrtc.SessionDescription{
		Type: webrtc.SDPTypeOffer,
		SDP:  offerSDP,
	}

	dataChanOpen := make(chan bool, 1)
	dcMessages := make(chan webrtc.DataChannelMessage, 1)

	dcReady := make(chan *webrtc.DataChannel, 1)

	//Called when a data channel is created by the remote (I think)
	pc.OnDataChannel(func(dc *webrtc.DataChannel) {
		fmt.Fprintln(logOut, "Data Channel Created by Remote: "+dc.Label())
		if dc.Label() != managementChannelName {
			return
		}

		dc.OnMessage(func(msg webrtc.DataChannelMessage) {
			dcMessages <- msg
		})

		dc.OnOpen(func() {
			dataChanOpen <- true
			close(dataChanOpen)
		})

		dcReady <- dc
	})

	//When the remote (host for now) adds their audio or video track
	pc.OnTrack(pm.onTrack)

	if err := pc.SetRemoteDescription(remoteOffer); err != nil {
		return nil, nil, nil, nil, nil, err
	}

	if err := pm.addTracks(pc); err != nil {
		return nil, nil, nil, nil, nil, err
	}

	answer, err := pc.CreateAnswer(nil)
	if err != nil {
		return nil, nil, nil, nil, nil, err
	}

	gatherComplete := webrtc.GatheringCompletePromise(pc)
	if err := pc.SetLocalDescription(answer); err != nil {
		return nil, nil, nil, nil, nil, err
	}

	// wait for gathering of candidates to complete
	<-gatherComplete

	localAnswer := pc.LocalDescription()
	if localAnswer == nil {
		return nil, nil, nil, nil, nil, fmt.Errorf("Local answer could not be generated")
	}

	keepConnection = true

	return pc, dcReady, dataChanOpen, dcMessages, pm, nil
}

func applyAnswer(pc *webrtc.PeerConnection, SDP string) error {
	return pc.SetRemoteDescription(webrtc.SessionDescription{
		Type: webrtc.SDPTypeAnswer,
		SDP:  SDP,
	})
}

func getSDPStr(pc *webrtc.PeerConnection) string {
	return pc.LocalDescription().SDP
}

// TODO: Call this before creating offer (for host) and before creating answer but after setting remote (for attendee)
func AddVoiceTrack(pc *webrtc.PeerConnection) (
	*webrtc.TrackLocalStaticSample,
	*webrtc.RTPSender,
	error,
) {

	defaultAudioCapability :=
		webrtc.RTPCodecCapability{
			MimeType:  webrtc.MimeTypeOpus,
			ClockRate: 48000,
			Channels:  2,
		}

	track, err := webrtc.NewTrackLocalStaticSample(
		defaultAudioCapability,
		voiceAudioChannelName,
		voiceAudioChannelName+"_stream",
	)
	if err != nil {
		return nil, nil, err
	}

	sender, err := pc.AddTrack(track)
	if err != nil {
		return nil, nil, err
	}

	// Drain the incoming RTCP data until it errors
	// 		For now just discard the data,
	// not the audio data, only data about connection stability and performance
	// Use this to read Receiver Reports and updates on network conditions
	go func() {
		buf := make([]byte, 1500)
		for {
			//Use ReadRTCP later to get formatted info
			if _, _, err := sender.Read(buf); err != nil {
				return
			}
		}
	}()

	return track, sender, nil

}

// The screen stream track: H.264 at 90 kHz. Frames are written as whole access units (WriteSample) and pion
// splits them into RTP packets.
func AddScreenVideoTrack(pc *webrtc.PeerConnection) (*webrtc.TrackLocalStaticSample, error) {
	track, err := webrtc.NewTrackLocalStaticSample(
		webrtc.RTPCodecCapability{
			MimeType:    webrtc.MimeTypeH264,
			ClockRate:   90000,
			SDPFmtpLine: screenVideoFmtp,
		},
		screenVideoTrackID,
		screenVideoTrackID+"_stream",
	)
	if err != nil {
		return nil, err
	}

	sender, err := pc.AddTrack(track)
	if err != nil {
		return nil, err
	}

	// Drain RTCP the same way the voice track does, so receiver reports and NACKs don't back up
	go func() {
		buf := make([]byte, 1500)
		for {
			if _, _, err := sender.Read(buf); err != nil {
				return
			}
		}
	}()

	return track, nil
}

func relayIncomingAudioToBuffer(track *webrtc.TrackRemote, fb frameBuffer) error {

	//Playback will pause and wait if under minimum in buffer
	//Or skip over packets if over maximum
	const minimumBufferedPackets = 4 // -> 40ms delay
	const audioFrameDuration = 10 * time.Millisecond
	const maxBufferedPackets = 8 // -> 80ms
	const resetSize = (maxBufferedPackets + minimumBufferedPackets) / 2

	enc := shared_mem_lib.NewAudioEncoding(true) // Is_input is not important here
	enc.SetIncoming()

	if !fb.RequestEncoding(enc) {
		return fmt.Errorf("Failed to request encoding")
	}

	if !fb.AlignRingsToConfig() {
		return fmt.Errorf("Failed to align rings to config")
	}

	opts := []jitterbuffer.Option{
		jitterbuffer.WithMinimumPacketCount(minimumBufferedPackets),
		jitterbuffer.WithOverflowPacketCount(maxBufferedPackets),
	}

	jb := jitterbuffer.New(opts...)

	readDone := make(chan struct{}) //Will stop playing from jitter buffer when this closes

	go func() {
		defer close(readDone)
		firstPacket := true
		for {
			packet, _, err := track.ReadRTP()
			if err != nil {
				fmt.Println("Error reading incoming audio")
				return
			}
			fmt.Printf("Received audio packet from remote:\n\tSequence: %d\n", packet.SequenceNumber)

			sequenceNum := packet.Header.SequenceNumber
			if int16(sequenceNum-jb.PlayoutHead()) < 0 && !firstPacket {
				//We have already played past this packet, arrived too late
				//Don't push packet
				continue

			}
			jb.DiscardIfOverflow(resetSize)
			jb.Push(packet) //First push sets playout head
			firstPacket = false
		}
	}()

	go func() {
		ticker := time.NewTicker(audioFrameDuration)
		defer ticker.Stop()

		for {
			select {
			case <-readDone:
				return
			case <-ticker.C:
				packet, err := jb.Pop()
				if errors.Is(err, jitterbuffer.ErrNotFound) {
					//Next packet not yet received (but more are queued after it)
					jb.SetPlayoutHead(jb.PlayoutHead() + 1)
					continue
				}
				if errors.Is(err, jitterbuffer.ErrInvalidOperation) {
					//Queue empty
					continue
				}
				if errors.Is(err, jitterbuffer.ErrPopWhileBuffering) {
					//Buffer waiting until more data arrives
					continue
				}
				if err != nil {
					fmt.Printf("Error reading from audio jitter buffer: %v\n", err)
					return
				}

				gcFrame := shared_mem_lib.NewGCFrame(enc)
				gcFrame.SetData(packet.Payload)
				if !fb.PushFrames([]shared_mem_lib.GCFrame{gcFrame}) {
					fmt.Println("Error pushing audio frame to shared buffer")
					gcFrame.Close()
					return
				}
				gcFrame.Close()
			}
		}
	}()

	return nil
}

// Add way to stop sending frames?
// Add a channel to signal something has failed?
func relayOutgoingAudioToRemote(track *webrtc.TrackLocalStaticSample, fb frameBuffer, enc shared_mem_lib.Encoding) error {
	const timeOutMs = 100000
	const maxAudioFrames = 100 // 10ms per frame -> 1 seconds

	//Hard code for now
	const audioFrameDuration = 10 * time.Millisecond

	go func() {
		var err error
		var ok bool
		for err == nil {
			ok, err = popAndCopyFrames(track, fb, enc, maxAudioFrames, timeOutMs, audioFrameDuration)
			if !ok {
				<-time.After(audioFrameDuration / 2)
			}
		}

		//Will have error if we exited the loop
		fmt.Println("Error relaying outgoing audio: " + err.Error())
	}()

	return nil
}

func popAndCopyFrames(
	track *webrtc.TrackLocalStaticSample, fb frameBuffer, enc shared_mem_lib.Encoding,
	maxAudioFrames uint32, timeOutMs uint32, audioFrameDuration time.Duration) (bool, error) {

	var framesToSend []shared_mem_lib.GCFrame
	ok, framesToSend := fb.BlockingPopFrames(enc, maxAudioFrames, timeOutMs) //BlockingPopFrames resizes slice to only be as long as needed

	if !ok {
		return false, nil
	}

	//Default value of bool is false, no need to initialize
	var closed = make([]bool, len(framesToSend))
	defer closeAll(framesToSend, closed)

	// fmt.Println("Received frames to send from processing module")

	var err error
	for i := 0; i < len(framesToSend) && err == nil; i++ {

		framesCopy := make([]byte, len(framesToSend[i].Bytes))
		copy(framesCopy, framesToSend[i].Bytes)
		framesToSend[i].Close()
		closed[i] = true

		var media media.Sample = media.Sample{
			Data:     framesCopy,
			Duration: audioFrameDuration,
		}
		err = track.WriteSample(media)
	}

	return true, err
}

func closeAll(frames []shared_mem_lib.GCFrame, closed []bool) {
	for i, fr := range frames {
		if !closed[i] {
			fr.Close()
		}
	}
}

func relayAudioOnceConnected(localTrack *webrtc.TrackLocalStaticSample, remoteTrackChan chan *webrtc.TrackRemote, fb frameBuffer) error {

	goAndPrintIfError(func() error {
		enc, err := waitForAudioEncodingFromProcessing(fb)
		if err != nil {
			return err
		}

		err = relayOutgoingAudioToRemote(localTrack, fb, enc)
		if err != nil {
			return err
		}
		return nil
	})

	goAndPrintIfError(func() error {
		remoteTrack := <-remoteTrackChan

		err := relayIncomingAudioToBuffer(remoteTrack, fb)
		if err != nil {
			return err
		}

		return nil
	})

	return nil
}

// Starts every relay between this peer connection and shared memory: voice and screen video, both directions.
// Doesn't block, the relays run on their own goroutines
func relayMediaOnceConnected(m *peerMedia, fb frameBuffer) {
	goAndPrintIfError(func() error { return relayAudioOnceConnected(m.VoiceTrack, m.RemoteVoice, fb) })

	go watchOutgoingVideo(m, fb)

	goAndPrintIfError(func() error {
		select {
		case remoteTrack := <-m.RemoteVideo:
			return relayIncomingVideoToBuffer(remoteTrack, fb)
		case <-m.Closed:
			return nil
		}
	})
}

// Polls shared memory for a Video + Outgoing encoding (the frontend registers one the first time you stream) and
// starts a relay for each new one. Runs until the connection closes
func watchOutgoingVideo(m *peerMedia, fb frameBuffer) {
	var relaying []shared_mem_lib.Encoding

	for {
		if encs, ok := fb.ReadEncodings(10); ok {
			for _, enc := range encs {
				if !enc.IsVideo() || !enc.IsOutgoing() || containsEncoding(relaying, enc) {
					continue
				}
				relaying = append(relaying, enc)
				fb.AlignRingsToConfig()
				go func() {
					if err := relayOutgoingVideoToRemote(m, fb, enc); err != nil {
						fmt.Fprintln(logOut, "Error relaying outgoing video: "+err.Error())
					}
				}()
			}
		}

		select {
		case <-m.Closed:
			return
		case <-time.After(500 * time.Millisecond):
		}
	}
}

func containsEncoding(encs []shared_mem_lib.Encoding, enc shared_mem_lib.Encoding) bool {
	for _, e := range encs {
		if shared_mem_lib.EncodingEq(e, enc) {
			return true
		}
	}
	return false
}

// Pops one access unit at a time off the encoding's ring and sends it. One at a time because the wrapper pins a
// whole 500 KB buffer for every frame it is asked for
func relayOutgoingVideoToRemote(m *peerMedia, fb frameBuffer, enc shared_mem_lib.Encoding) error {
	for {
		select {
		case <-m.Closed:
			return nil
		default:
		}

		ok, frames := fb.BlockingPopFrames(enc, 1, 500)
		if !ok {
			// The real pop waits out its timeout, but it fails at once if the ring isn't there yet
			time.Sleep(5 * time.Millisecond)
			continue
		}

		for i := range frames {
			data := make([]byte, len(frames[i].Bytes))
			copy(data, frames[i].Bytes)
			frames[i].Close()

			err := m.VideoTrack.WriteSample(media.Sample{Data: data, Duration: videoFrameDuration})
			if err != nil {
				return err
			}
		}
	}
}

// Rebuilds access units from the remote's RTP packets and pushes each into a Video + Incoming ring, with a
// delimiter in front (see audBytes). Access units over one shared memory slot are dropped
func relayIncomingVideoToBuffer(track *webrtc.TrackRemote, fb frameBuffer) error {
	enc := shared_mem_lib.NewVideoEncoding(videoBitrateKbps, videoBitrateKbps, 720, 1280, 30)
	enc.SetIncoming()

	if !fb.RequestEncoding(enc) {
		return fmt.Errorf("Failed to request video encoding")
	}

	if !fb.AlignRingsToConfig() {
		return fmt.Errorf("Failed to align rings to config")
	}

	sb := samplebuilder.New(256, &codecs.H264Packet{}, 90000)

	for {
		packet, _, err := track.ReadRTP()
		if err != nil {
			return fmt.Errorf("Error reading incoming video: %w", err)
		}

		sb.Push(packet)

		for sample := sb.Pop(); sample != nil; sample = sb.Pop() {
			frame := make([]byte, 0, len(audBytes)+len(sample.Data))
			frame = append(frame, audBytes...)
			frame = append(frame, sample.Data...)

			if len(frame) > maxVideoFrameBytes {
				fmt.Fprintf(logOut, "Dropped a %d byte video frame, a slot holds %d\n", len(frame), maxVideoFrameBytes)
				continue
			}

			gcFrame := shared_mem_lib.NewGCFrameWithData(enc, frame)
			pushed := fb.PushFrames([]shared_mem_lib.GCFrame{gcFrame})
			gcFrame.Close()
			if !pushed {
				// Ring full: the frontend fell behind. Dropping is fine, the next keyframe catches it up
				fmt.Fprintln(logOut, "Video ring full, dropped a frame")
			}
		}
	}
}

// Generated with AI
func logPeerConnectionState(pc *webrtc.PeerConnection, role string, m *peerMedia) {
	pc.OnICECandidate(func(candidate *webrtc.ICECandidate) {
		if candidate == nil {
			fmt.Fprintf(logOut, "[%s] ICE candidate gathering complete\n", role)
			return
		}
		fmt.Fprintf(logOut, "[%s] Local ICE candidate: %s\n", role, candidate.String())
	})

	pc.OnICEConnectionStateChange(func(state webrtc.ICEConnectionState) {
		fmt.Fprintf(logOut, "[%s] ICE connection state: %s\n", role, state.String())
	})

	pc.OnConnectionStateChange(func(state webrtc.PeerConnectionState) {
		fmt.Fprintf(logOut, "[%s] Peer connection state: %s\n", role, state.String())
		if state == webrtc.PeerConnectionStateClosed || state == webrtc.PeerConnectionStateFailed {
			m.markClosed()
		}
	})

	pc.OnSignalingStateChange(func(state webrtc.SignalingState) {
		fmt.Fprintf(logOut, "[%s] Signaling state: %s\n", role, state.String())
	})
}
