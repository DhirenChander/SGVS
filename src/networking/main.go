package main

import (
	"bufio"
	"fmt"
	"os"
	"reflect"
	"shared_mem_lib"
	"time"

	"github.com/pion/webrtc/v4"
)

// type frameBuffer *shared_mem_lib.FrameBuffer

// Allows mocking of frameBuffer
type frameBuffer interface {
	PushFrames([]shared_mem_lib.GCFrame) bool
	BlockingPopFrames(
		shared_mem_lib.Encoding,
		uint32,
		uint32,
	) (bool, []shared_mem_lib.GCFrame)
	PushMessage(msg *shared_mem_lib.Message) bool
	BlockingPopMessage(src, dst shared_mem_lib.Module, max_timeout uint32) (shared_mem_lib.Message, bool)
	ReadEncodings(maxLen uint8) ([]shared_mem_lib.Encoding, bool)
	RequestEncoding(enc shared_mem_lib.Encoding) bool
	AlignRingsToConfig() bool
}

func main() {
	var fb shared_mem_lib.FrameBuffer
	ok := shared_mem_lib.NewFrameBuffer(&fb)

	if !ok {
		fmt.Println("Failed to create frame buffer")
		return
	}

	defer fb.Free()

	pc, _, _, _, err := startCallOnMessage(&fb)

	//TODO: print messages on datachannel open?

	if err != nil {
		fmt.Println("Error starting call: " + err.Error())
		//Retry?
	}

	//Temp to keep pc open during testing
	//TODO: change to wait on message from application to end call
	<-time.After(5 * time.Minute)
	_ = pc.Close()
}

// Waits for a message from the application and then sets up the call, either as the host or the attendee
func startCallOnMessage(fb frameBuffer) (
	*webrtc.PeerConnection,
	chan *webrtc.DataChannel,
	chan bool, //Signals dataChannel is open
	chan webrtc.DataChannelMessage,
	error,
) {
	host := true
	offerSDP := "" //Set if message that we are attendee, and gives SDP

	//TODO: Wait for message from application
	for {
		fmt.Println("Waiting for messages...")
		msg, succ := fb.BlockingPopMessage(shared_mem_lib.Application, shared_mem_lib.Network, 100*1000)

		if succ {
			fmt.Println("Got message:", msg.GetOperation())

			if msg.GetOperation() == shared_mem_lib.StartHost {
				host = true
				break
			} else if msg.GetOperation() == shared_mem_lib.JoinCall {
				host = false
				data, err := msg.UnmarshallMessage(reflect.TypeFor[string]())

				if err != nil {
					fmt.Println("Unable to unmarshall message: %w", err)
				}

				offerSDP = data[0].(string)

				break
			}
		}
	}

	if host {
		peerConnection, dataChan, dataChanOpen, messages, err := setupPcAsHost(fb)

		dcReady := make(chan *webrtc.DataChannel, 1)
		dcReady <- dataChan

		return peerConnection, dcReady, dataChanOpen, messages, err

	} else {
		return setupPcAsAttendee(fb, offerSDP)
	}

}

func setupPcAsHost(fb frameBuffer) (
	*webrtc.PeerConnection,
	*webrtc.DataChannel,
	chan bool, //Signals when dataChannel is open
	chan webrtc.DataChannelMessage, //Messages are sent here
	error) {

	fmt.Println("Setting up host")

	offerPc, dataChan, dataChanOpen, messages,
		selfVoiceTrack, _, remoteVoiceTrackChan, _, //Use receivers later to judge network conditions
		err := generateOffer()

	if err != nil {
		return nil, nil, nil, nil, fmt.Errorf("Error generating offer: %s\n", err)
	}

	fmt.Println("Offer SDP:")
	fmt.Println(getSDPStr(offerPc))

	keepConnection := false
	defer func() {
		if !keepConnection {
			//If setup fails we may get an error on close, but we can ignore it
			_ = offerPc.Close()
		}
	}()

	//TODO: Send SDP to application for message from application
	//sdpStr := getSDPStr(offerPc)
	reader := bufio.NewReader(os.Stdin)
	fmt.Printf("\nEnter Answer SDP:\n")
	answerStr, err := readSDP(reader)

	//TODO: Wait for peer SDP from application
	// answerStr, err := "", nil

	for {
		fmt.Println("Waiting for answer SDP...")
		msg, succ := fb.BlockingPopMessage(shared_mem_lib.Application, shared_mem_lib.Network, 100*1000)

		if succ {
			if msg.GetOperation() == shared_mem_lib.AnswerCode {
				data, err := msg.UnmarshallMessage(reflect.TypeFor[string]())

				if err != nil {
					fmt.Println("Unable to unmarshall message: %w", err)
				}

				answerStr = data[0].(string)
				break
			}
		}
	}

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

func setupPcAsAttendee(fb frameBuffer, SDPStr string) (
	*webrtc.PeerConnection,
	chan *webrtc.DataChannel,
	chan bool, //Signals when dataChannel is open (by closing itself)
	chan webrtc.DataChannelMessage, //Messages are sent here
	error) {

	fmt.Println("Setting up attendee")

	answerPc, dcReady, dataChanOpen, messages,
		selfVoiceTrack, _, remoteVoiceTrackChan, _,
		err := generateAnswer(SDPStr)

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

	//TODO: Send SDP to application for message from application

	//Send voice audio
	goAndPrintIfError(func() error { return relayAudioOnceConnected(selfVoiceTrack, remoteVoiceTrackChan, fb) })

	keepConnection = true
	return answerPc, dcReady, dataChanOpen, messages, nil
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
