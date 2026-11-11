package main

import (
	"fmt"
	"strings"

	"shared_mem_lib"

	"github.com/pion/webrtc/v4/pkg/media"

	"github.com/pion/webrtc/v4"
)

const managementChannelName = "transceiver_management"
const voiceAudioChannelName = "voice_audio"

var StunServers = []string{
	"stun.l.google.com:19302",
	"stun1.l.google.com:19302",
	"stun2.l.google.com:19302",
	"stun3.l.google.com:19302",
	"stun4.l.google.com:19302",
}

var ICEConfiguration = webrtc.Configuration{
	ICEServers: []webrtc.ICEServer{{
		URLs: StunServers,
	}},
}

// Setting up connections
// Switch to named return?
// Or wrap objects together in a struct?
func generateOffer() (
	*webrtc.PeerConnection,
	*webrtc.DataChannel,
	chan bool, //Signals when dataChannel is open
	chan webrtc.DataChannelMessage, //Messages are sent to here (should this be an input instead of output?)
	*webrtc.TrackLocalStaticSample, //Voice audio track
	*webrtc.RTPSender, //Voice audio sender
	chan *webrtc.TrackRemote, //Voice audio channel
	chan *webrtc.RTPReceiver, //Voice audio receiver
	error) {

	// create a peer connection
	pc, err := webrtc.NewPeerConnection(ICEConfiguration)
	if err != nil {
		return nil, nil, nil, nil, nil, nil, nil, nil, err
	}
	logPeerConnectionState(pc, "host")

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
		_ = pc.Close()
		return nil, nil, nil, nil, nil, nil, nil, nil, err
	}

	voiceTrack, voiceSender, err := AddVoiceTrack(pc)

	remoteAudioChan := make(chan *webrtc.TrackRemote, 1)
	remoteAudioReceiverChan := make(chan *webrtc.RTPReceiver, 1)

	//When the remote adds their audio track
	pc.OnTrack(func(remoteTrack *webrtc.TrackRemote, receiver *webrtc.RTPReceiver) {
		if remoteTrack.ID() != voiceAudioChannelName {
			return
		}

		remoteAudioChan <- remoteTrack
		remoteAudioReceiverChan <- receiver
	})

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
		return nil, nil, nil, nil, nil, nil, nil, nil, err
	}

	// gatherComplete is a channel that will be closed when
	// the gathering of local candidates is complete.
	gatherComplete := webrtc.GatheringCompletePromise(pc)

	// apply the offer
	err = pc.SetLocalDescription(offer)
	if err != nil {
		return nil, nil, nil, nil, nil, nil, nil, nil, err
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
		return nil, nil, nil, nil, nil, nil, nil, nil, fmt.Errorf("No SDP candidates found, offer is not usable")
	}

	keepConnection = true
	return pc, dataChannel, dataChanOpen, dcMessages,
		voiceTrack, voiceSender, remoteAudioChan, remoteAudioReceiverChan,
		nil
}

func generateAnswer(offerSDP string) (
	*webrtc.PeerConnection,
	chan *webrtc.DataChannel, //Return a channel that will recieve the DataChannel once it is created
	//                          Because channel is only received once host finished setup, we cannot wait for channel here

	chan bool, //Signals when dataChannel is open
	chan webrtc.DataChannelMessage, //Messages are sent to here (should this be an input instead of output?)
	*webrtc.TrackLocalStaticSample, //Voice audio track
	*webrtc.RTPSender, //Voice audio sender
	chan *webrtc.TrackRemote, //Voice audio channel
	chan *webrtc.RTPReceiver, //Voice audio receiver

	error) {

	pc, err := webrtc.NewPeerConnection(ICEConfiguration)
	if err != nil {
		return nil, nil, nil, nil, nil, nil, nil, nil, err
	}
	logPeerConnectionState(pc, "attendee")

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

	remoteAudioChan := make(chan *webrtc.TrackRemote, 1)
	remoteAudioReceiverChan := make(chan *webrtc.RTPReceiver, 1)

	//When the remote (host for now) adds their audio track
	pc.OnTrack(func(remoteTrack *webrtc.TrackRemote, receiver *webrtc.RTPReceiver) {
		if remoteTrack.ID() != voiceAudioChannelName {
			return
		}

		remoteAudioChan <- remoteTrack
		remoteAudioReceiverChan <- receiver
	})

	if err := pc.SetRemoteDescription(remoteOffer); err != nil {
		return nil, nil, nil, nil, nil, nil, nil, nil, err
	}

	voiceTrack, voiceSender, err := AddVoiceTrack(pc)
	if err != nil {
		return nil, nil, nil, nil, nil, nil, nil, nil, err
	}

	answer, err := pc.CreateAnswer(nil)
	if err != nil {
		return nil, nil, nil, nil, nil, nil, nil, nil, err
	}

	gatherComplete := webrtc.GatheringCompletePromise(pc)
	if err := pc.SetLocalDescription(answer); err != nil {
		return nil, nil, nil, nil, nil, nil, nil, nil, err
	}

	// wait for gathering of candidates to complete
	<-gatherComplete

	localAnswer := pc.LocalDescription()
	if localAnswer == nil {
		return nil, nil, nil, nil, nil, nil, nil, nil, fmt.Errorf("Local answer could not be generated")
	}

	keepConnection = true

	return pc, dcReady, dataChanOpen, dcMessages,
		voiceTrack, voiceSender, remoteAudioChan, remoteAudioReceiverChan,
		nil
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

func relayIncomingAudioToBuffer(track *webrtc.TrackRemote, fb *shared_mem_lib.FrameBuffer) error {

	var enc shared_mem_lib.Encoding
	ok := fb.RequestEncoding(enc)
	if !ok {
		return fmt.Errorf("Failed to get encoding")
	}

	go func() {
		for {
			packet, _, err := track.ReadRTP()
			if err != nil {
				fmt.Println("Error reading incoming audio")
				return
			}

			//Split into segments?
			//Currently puts everything into a single frame
			gcFrame := shared_mem_lib.NewGCFrame(enc)
			gcFrame.Bytes = packet.Payload

			//cast to array of frames
			allFrames := []shared_mem_lib.GCFrame{gcFrame}

			fb.PushFrames(allFrames)
		}
	}()

	return nil
}

// Add way to stop sending frames?
// Add a channel to signal something has failed?
func relayOutgoingAudioToRemote(track *webrtc.TrackLocalStaticSample, fb *shared_mem_lib.FrameBuffer) error {

	const timeOutMs = 10000
	const maxAudioFrames = 1000

	var enc shared_mem_lib.Encoding
	ok := fb.RequestEncoding(enc)

	if !ok {
		return fmt.Errorf("Failed to get encoding")
	}

	go func() {
		var err error
		for ok, err = true, nil; ok && err == nil; {
			var framesToSend []shared_mem_lib.GCFrame = make([]shared_mem_lib.GCFrame, maxAudioFrames)
			ok, framesToSend = fb.BlockingPopFrames(enc, maxAudioFrames, timeOutMs) //BlockingPopFrames resizes slice to only be as long as needed

			for i := 0; i < len(framesToSend); i++ {
				var media media.Sample = media.Sample{
					Data: framesToSend[i].Bytes,
					//Do we need timestamp and duration info?
				}
				err = track.WriteSample(media)
				if err != nil {
					break
				}
			}
		}

		if !ok {
			fmt.Println("Error popping audio frames")
		}

		if err != nil {
			fmt.Println("Error relaying outgoing audio: " + err.Error())
		}
	}()

	return nil
}

// Generated with AI
func logPeerConnectionState(pc *webrtc.PeerConnection, role string) {
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
	})

	pc.OnSignalingStateChange(func(state webrtc.SignalingState) {
		fmt.Printf("[%s] Signaling state: %s\n", role, state.String())
	})
}
