package main

import (
	"fmt"
	"strings"

	"github.com/pion/webrtc/v4"
)

const managementChannelName = "transceiver_management"

// Setting up connections
func generateOffer() (
	*webrtc.PeerConnection,
	*webrtc.DataChannel,
	chan struct{}, //Signals when dataChannel is open (by closing itself)
	chan webrtc.DataChannelMessage, //Messages are sent to here (should this be an input instead of output?)
	error) {

	// create a peer connection
	pc, err := webrtc.NewPeerConnection(webrtc.Configuration{})
	if err != nil {
		return nil, nil, nil, nil, err
	}

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
		return nil, nil, nil, nil, err
	}

	dataChanOpen := make(chan struct{}, 1)
	dcMessages := make(chan webrtc.DataChannelMessage, 1)

	dataChannel.OnMessage(func(msg webrtc.DataChannelMessage) {
		dcMessages <- msg
	})

	dataChannel.OnOpen(func() {
		//Close channel to signal dataChannel is open
		close(dataChanOpen)

	})

	// create a first offer that does not contain any local candidates
	offer, err := pc.CreateOffer(nil)
	if err != nil {
		return nil, nil, nil, nil, err
	}

	// gatherComplete is a channel that will be closed when
	// the gathering of local candidates is complete.
	gatherComplete := webrtc.GatheringCompletePromise(pc)

	// apply the offer
	err = pc.SetLocalDescription(offer)
	if err != nil {
		return nil, nil, nil, nil, err
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
		return nil, nil, nil, nil, fmt.Errorf("No SDP candidates found, offer is not usable")
	}

	keepConnection = true
	return pc, dataChannel, dataChanOpen, dcMessages, nil
}

func generateAnswer(offerSDP string) (
	*webrtc.PeerConnection,
	*webrtc.DataChannel,
	chan struct{}, //Signals when dataChannel is open (by closing itself)
	chan webrtc.DataChannelMessage, //Messages are sent to here (should this be an input instead of output?)
	error) {

	pc, err := webrtc.NewPeerConnection(webrtc.Configuration{})
	if err != nil {
		return nil, nil, nil, nil, err
	}

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

	dataChanOpen := make(chan struct{}, 1)
	dcMessages := make(chan webrtc.DataChannelMessage, 1)
	dcReady := make(chan *webrtc.DataChannel, 1)

	//Called when a data channel is created by the remote (I think)
	pc.OnDataChannel(func(dc *webrtc.DataChannel) {
		fmt.Println("Data Channel Opened: " + dc.Label())
		if dc.Label() != managementChannelName {
			return
		}

		dc.OnMessage(func(msg webrtc.DataChannelMessage) {
			dcMessages <- msg
		})

		dc.OnOpen(func() {
			//Signal that the data channel is open by closing this channel
			close(dataChanOpen)
		})

		dcReady <- dc
	})

	if err := pc.SetRemoteDescription(remoteOffer); err != nil {
		return nil, nil, nil, nil, err
	}

	answer, err := pc.CreateAnswer(nil)
	if err != nil {
		return nil, nil, nil, nil, err
	}

	gatherComplete := webrtc.GatheringCompletePromise(pc)
	if err := pc.SetLocalDescription(answer); err != nil {
		return nil, nil, nil, nil, err
	}

	// wait for gathering of candidates to complete
	<-gatherComplete

	localAnswer := pc.LocalDescription()
	if localAnswer == nil {
		return nil, nil, nil, nil, fmt.Errorf("Local answer could not be generated")
	}

	fmt.Println("Waiting for Datachannel creation")
	dc := <-dcReady

	keepConnection = true
	return pc, dc, dataChanOpen, dcMessages, nil
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
