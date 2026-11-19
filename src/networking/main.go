package main

import (
	"bufio"
	"fmt"
	"os"
	"shared_mem_lib"
	"time"

	"github.com/pion/webrtc/v4"
)

// Allows mocking of frameBuffer
type frameBuffer interface {
	PushFrames([]shared_mem_lib.GCFrame) bool
	BlockingPopFrames(
		shared_mem_lib.Encoding,
		uint32,
		uint32,
	) (bool, []shared_mem_lib.GCFrame)

	ReadEncodings(maxLen uint8) ([]shared_mem_lib.Encoding, bool)
	RequestEncoding(enc shared_mem_lib.Encoding) bool
	AlignRingsToConfig() bool
}

func main() {
	args := os.Args[1:]

	var fb shared_mem_lib.FrameBuffer
	ok := shared_mem_lib.NewFrameBuffer(&fb)

	defer fb.Free()

	if !ok {
		fmt.Println("Failed to create frame buffer")
		return
	}

	//Check if user wants to create a connection or answer
	host := (len(args) >= 1 && args[0] == "host")

	pcChan := make(chan *webrtc.PeerConnection, 1)

	go func() {
		pcChan <- testCallAndResponse(host, &fb)
	}()

	<-time.After(5 * time.Minute)
	<-pcChan //Used to keep peer connection open during testing

}

func testCallAndResponse(host bool, fb *shared_mem_lib.FrameBuffer) *webrtc.PeerConnection {
	if host /* We are the host */ {

		peerConnection, dataChan, dataChanOpen, messages, err := setupPcAsHost(fb)

		if err != nil {
			fmt.Printf("Error setting up connection: %s\n", err)
			return nil
		}

		//Wait for data channel to open
		fmt.Println("Waiting for datachannel to open")
		<-dataChanOpen

		fmt.Println("Sending message from host")
		dataChan.SendText("Hello from host :)")

		//Wait for response
		fmt.Println("Waiting for response")
		response := <-messages

		if !response.IsString {
			fmt.Printf("Message was not a string")
			return nil
		}

		fmt.Printf("Recieved Message: %s\n", string(response.Data))

		return peerConnection

	} else /* We are not the host */ {

		peerConnection, dcReady, dataChanOpen, messages, err := setupPcAsAttendee(fb)

		if err != nil {
			fmt.Printf("Error setting up connection: %s\n", err)
			return nil
		}

		fmt.Println("Waiting for datachannel to be created")
		dataChan := <-dcReady

		fmt.Println("Waiting for datachannel to open")
		<-dataChanOpen

		fmt.Println("Waiting for message from host")
		firstMessage := <-messages

		if !firstMessage.IsString {
			fmt.Printf("Message was not a string")
			return nil
		}

		fmt.Printf("Recieved Message: %s\n", string(firstMessage.Data))

		fmt.Println("Sending reponse")

		dataChan.SendText("Hello from the other side")

		return peerConnection
	}

}

func setupPcAsHost(fb *shared_mem_lib.FrameBuffer) (
	*webrtc.PeerConnection,
	*webrtc.DataChannel,
	chan bool, //Signals when dataChannel is open
	chan webrtc.DataChannelMessage, //Messages are sent here
	error) {

	// Change to take in a reader and writer if using a different IO stream
	reader := bufio.NewReader(os.Stdin)

	offerPc, dataChan, dataChanOpen, messages,
		selfVoiceTrack, _, remoteVoiceTrackChan, _, //Use receivers later to judge network conditions
		err := generateOffer()

	if err != nil {
		return nil, nil, nil, nil, fmt.Errorf("Error generating offer: %s\n", err)
	}

	keepConnection := false
	defer func() {
		if !keepConnection {
			//If setup fails we may get an error on close, but we can ignore it
			_ = offerPc.Close()
		}
	}()

	fmt.Printf("Offer SDP:\n%s", getSDPStr(offerPc))

	//Prompt user for answer string and wait
	fmt.Printf("\nEnter Answer SDP:\n")
	answerStr, err := readSDP(reader)
	if err != nil {

		return nil, nil, nil, nil, fmt.Errorf("Error reading answer: %s\n", err)
	}

	//apply answer to connection
	err = applyAnswer(offerPc, answerStr)
	if err != nil {
		return nil, nil, nil, nil, fmt.Errorf("Error applying answer: %s\n", err)
	}

	//Send voice audio
	goAndPrintIfError(func() error { return relayAudioOnceConnected(selfVoiceTrack, remoteVoiceTrackChan, fb) })

	keepConnection = true
	return offerPc, dataChan, dataChanOpen, messages, nil
}

func setupPcAsAttendee(fb *shared_mem_lib.FrameBuffer) (
	*webrtc.PeerConnection,
	chan *webrtc.DataChannel,
	chan bool, //Signals when dataChannel is open (by closing itself)
	chan webrtc.DataChannelMessage, //Messages are sent here
	error) {

	reader := bufio.NewReader(os.Stdin)

	//Wait for user input
	fmt.Print("Enter connection string:\n\n")
	SDPOfferStr, err := readSDP(reader)
	if err != nil {
		return nil, nil, nil, nil, fmt.Errorf("Error reading offer: %s\n", err)
	}

	answerPc, dcReady, dataChanOpen, messages,
		selfVoiceTrack, _, remoteVoiceTrackChan, _,
		err := generateAnswer(SDPOfferStr)

	keepConnection := false
	defer func() {
		if !keepConnection {
			//If setup fails we may get an error on close, but we can ignore it
			_ = answerPc.Close()
		}
	}()

	if err != nil {
		return nil, nil, nil, nil, fmt.Errorf("Error generating answer: %s\n", err)
	}

	fmt.Printf("Answer SDP:\n%s", getSDPStr(answerPc))

	//Send voice audio
	goAndPrintIfError(func() error { return relayAudioOnceConnected(selfVoiceTrack, remoteVoiceTrackChan, fb) })

	keepConnection = true
	return answerPc, dcReady, dataChanOpen, messages, nil
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

func waitForAudioEncodingFromProcessing(fb frameBuffer) (shared_mem_lib.Encoding, error) {
	const timeOut = 100 * time.Second
	const interval = 500 * time.Millisecond
	elapsed := 0 * time.Second

	for elapsed < timeOut {
		encs, succ := fb.ReadEncodings(10)
		if succ {
			fb.AlignRingsToConfig()
			// fmt.Println("Got: ", len(encs), " Encodings")
			for _, v := range encs {
				if v.IsOutgoing() && v.IsAudio() {
					return v, nil
				}
			}
		} else {
			fmt.Println("Failed to poll encodings")
		}

		elapsed += interval
		time.Sleep(interval)
	}

	return shared_mem_lib.Encoding{}, fmt.Errorf("Did not find audio encoding after %s", string(timeOut))
}

func goAndPrintIfError(f func() error) {
	go func() {
		err := f()
		if err != nil {
			fmt.Println("Error in concurrent func: " + err.Error())
		}
	}()
}

// Read SDP string from input stream (console for now)
func readSDP(reader *bufio.Reader) (string, error) {
	SDPStr := ""
	for {
		ln, err := reader.ReadString('\n')
		if err != nil {
			return SDPStr, err
		}
		if ln == "\r\n" || ln == "\n" {
			return SDPStr, nil
		}
		SDPStr += ln
	}
}
