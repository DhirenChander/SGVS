package main

import (
	"fmt"
	"strings"

	"github.com/pion/webrtc/v4"
)

// Setting up connections
func generateOffer() (*webrtc.PeerConnection, error) {
	// create a peer connection
	pc, err := webrtc.NewPeerConnection(webrtc.Configuration{})
	if err != nil {
		return nil, err
	}

	// Add Transceivers
	// This could use pc.AddTrack instead.
	// pc.AddTrack(&webrtc.TrackLocalStaticRTP{})
	_, err = pc.AddTransceiverFromKind(webrtc.RTPCodecTypeAudio)
	if err != nil {
		return nil, err
	}

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

func generateAnswer(offerSDP string) (*webrtc.PeerConnection, error) {
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

	keepConnection = true
	return pc, nil
}

func getSDPStr(connection *webrtc.PeerConnection) string {
	return connection.LocalDescription().SDP
}
