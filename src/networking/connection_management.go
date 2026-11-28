package main

import (
	"fmt"
	"strings"
	"sync"
	"time"

	"shared_mem_lib"

	"github.com/pion/webrtc/v4"
)

const managementChannelName = "transceiver_management"

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
	//TODO: add RTP senders and receivers
	//TODO: add stream audio

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
	case voiceAudioTrackId:
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
		fmt.Println("Data Channel Created by Remote: " + dc.Label())
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

// Starts every relay between this peer connection and shared memory: voice and screen video, both directions.
// Doesn't block, the relays run on their own goroutines
func relayMediaOnceConnected(m *peerMedia, fb frameBuffer) {
	goAndPrintIfError(func() error { return relayAudioOnceConnected(m.VoiceTrack, m.RemoteVoice, fb) })

	// May interfere with main who will soon listen for SDP string
	// TODO: Merge into one function that sends messages to correct channels
	// go listenForApplicationMessages(m, fb)

	go watchForOutgoingVideoEncs(m, fb)

	goAndPrintIfError(func() error {
		select {
		case remoteTrack := <-m.RemoteVideo:
			return relayIncomingVideoToBuffer(remoteTrack, fb)
		case <-m.Closed:
			return nil
		}
	})
}

func listenForApplicationMessages(m *peerMedia, fb frameBuffer) {
	const popTimeoutMs = 5000
	for {
		select {
		case <-m.Closed:
			return
		default:
		}

		msg, ok := fb.BlockingPopMessage(shared_mem_lib.Application, shared_mem_lib.Network, popTimeoutMs)
		if !ok {
			continue
		}
		switch msg.GetOperation() {
		case shared_mem_lib.MicMuted:
			updateMicMuted(m, true)
		case shared_mem_lib.MicUnmuted:
			updateMicMuted(m, false)
			//add more messages here...
		}
	}
}

func updateMicMuted(m *peerMedia, micMuted bool) {
	//TODO: Send message to peer that mic mute status changed
	if micMuted {
		fmt.Println("Mic muted")
		return
	}
	fmt.Println("Mic unmuted")

}

// TODO: move to separate helper file?
func containsEncoding(encs []shared_mem_lib.Encoding, enc shared_mem_lib.Encoding) bool {
	for _, e := range encs {
		if shared_mem_lib.EncodingEq(e, enc) {
			return true
		}
	}
	return false
}

// Generated with AI
func logPeerConnectionState(pc *webrtc.PeerConnection, role string, m *peerMedia) {
	pc.OnICECandidate(func(candidate *webrtc.ICECandidate) {
		if candidate == nil {
			fmt.Printf("[%s] ICE candidate gathering complete\n", role)
			return
		}
		fmt.Printf("[%s] Local ICE candidate: %s\n", role, candidate.String())
	})

	pc.OnICEConnectionStateChange(func(state webrtc.ICEConnectionState) {
		fmt.Printf("[%s] ICE connection state: %s\n", role, state.String())
	})

	pc.OnConnectionStateChange(func(state webrtc.PeerConnectionState) {
		fmt.Printf("[%s] Peer connection state: %s\n", role, state.String())
		if state == webrtc.PeerConnectionStateClosed || state == webrtc.PeerConnectionStateFailed {
			m.markClosed()
		}
	})

	pc.OnSignalingStateChange(func(state webrtc.SignalingState) {
		fmt.Printf("[%s] Signaling state: %s\n", role, state.String())
	})
}
