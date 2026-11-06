package main

import (
	"fmt"
	"strings"

	"github.com/pion/webrtc/v4"
)

const managementChannelName = "transceiver_management"

const testMessage = "Hello from Host :)"

// Setting up connections
func generateOffer(managementChannel chan webrtc.DataChannelMessage) (*webrtc.PeerConnection, error) {
	// create a peer connection
	pc, err := webrtc.NewPeerConnection(webrtc.Configuration{})
	if err != nil {
		return nil, err
	}

	//Start with initial data channel for sending application messages
	//This will let us add appropriate transcievers after the connection is established
	dataChannel, err := pc.CreateDataChannel(managementChannelName, nil)
	if err != nil {
		_ = pc.Close()
		return nil, err
	}

	dataChannel.OnOpen(func() {
		fmt.Println("Signaling data channel is open, sending test message")
		// Send data over connection

		err = dataChannel.SendText(testMessage)
		if err != nil {
			fmt.Printf("Error sending test message: %s\n", err)
		}
	})

	dataChannel.OnMessage(func(msg webrtc.DataChannelMessage) {
		managementChannel <- msg
	})

	// create a first offer that does not contain any local candidates
	offer, err := pc.CreateOffer(nil)
	if err != nil {
		return nil, err
	}

	// gatherComplete is a channel that will be closed when
	// the gathering of local candidates is complete.
	gatherComplete := webrtc.GatheringCompletePromise(pc)

	// apply the offer
	err = pc.SetLocalDescription(offer)
	if err != nil {
		return nil, err
	}

	// wait for gathering of local candidates to complete
	<-gatherComplete

	// compute the local offer again
	offer2 := pc.LocalDescription()

	// This second offer contains all candidates, and can be sent to peer
	// Since we don't trickle SDPs, and grab them all upfront, we only need one message

	// Check that it contains at least one candidate
	// Potentially move this check outside of this function if we need to differentiate it or act differently
	if !strings.Contains(offer2.SDP, "\na=candidate:") {
		return nil, fmt.Errorf("No SDP candidates found, offer is not usable")
	}

	// fmt.Printf("Offer String:\n%s", offer2.SDP)

	//The description can be grabbed from the connection
	return pc, nil
}

func generateAnswer(offerSDP string, managementChannel chan webrtc.DataChannelMessage) (*webrtc.PeerConnection, error) {
	pc, err := webrtc.NewPeerConnection(webrtc.Configuration{})
	if err != nil {
		return nil, err
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

	if err := pc.SetRemoteDescription(remoteOffer); err != nil {
		return nil, err
	}

	answer, err := pc.CreateAnswer(nil)
	if err != nil {
		return nil, err
	}

	gatherComplete := webrtc.GatheringCompletePromise(pc)
	if err := pc.SetLocalDescription(answer); err != nil {
		return nil, err
	}

	// wait for gathering of candidates to complete
	<-gatherComplete

	localAnswer := pc.LocalDescription()
	if localAnswer == nil {
		return nil, fmt.Errorf("Local answer could not be generated")
	}

	//Called when a data channel is created by the remote (I think)
	pc.OnDataChannel(func(dc *webrtc.DataChannel) {
		if dc.Label() != managementChannelName {
			return
		}

		// When we receive messages on this channel, pass them along to parameter managementChannel
		dc.OnMessage(func(msg webrtc.DataChannelMessage) {
			managementChannel <- msg
		})
	})

	keepConnection = true
	return pc, nil
}

func applyAnswer(pc *webrtc.PeerConnection, SDP string) error {
	return pc.SetRemoteDescription(webrtc.SessionDescription{
		Type: webrtc.SDPTypeAnswer,
		SDP:  SDP,
	})
}

func getSDPStr(pc *webrtc.PeerConnection) string {
	return pc.LocalDescription().SDP //Change later
}
