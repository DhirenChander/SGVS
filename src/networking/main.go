package main

import (
	"bufio"
	"context"
	"fmt"
	"os"
	"os/signal"
	"shared_mem_lib"

	"github.com/pion/webrtc/v4"
)

// type frameBuffer *shared_mem_lib.FrameBuffer

// Allows mocking of frameBuffer
type frameBuffer interface {
	PushFrames(fs []shared_mem_lib.GCFrame) bool
	// BlockingPopFrames reallocates each frame and the buffer as a whole, to minimize space
	BlockingPopFrames(
		enc shared_mem_lib.Encoding,
		maxFrames uint32,
		maxTimeout uint32,
	) (bool, []shared_mem_lib.GCFrame)
	RequestEncoding(enc shared_mem_lib.Encoding) bool
	AlignRingsToConfig() bool
	PushMessage(msg *shared_mem_lib.Message) bool
	BlockingPopMessage(src, dst shared_mem_lib.Module, max_timeout uint32) (shared_mem_lib.Message, bool)
	ReadEncodings(maxLen uint8) ([]shared_mem_lib.Encoding, bool)
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
		return
	}

	// Keep the call open until the process is told to stop (Ctrl+C)
	//TODO: change to wait on message from application to end call
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt)
	defer stop()
	<-ctx.Done()
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
				/*data, err := msg.UnmarshallMessage(reflect.TypeFor[string]())

				if err != nil {
					fmt.Println("Unable to unmarshall message: %w", err)
				}

				offerSDP = data[0].(string)*/
				reader := bufio.NewReader(os.Stdin)
				fmt.Printf("\nEnter Answer SDP:\n")
				offerSDP, _ = readSDP(reader)

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

	offerPc, dataChan, dataChanOpen, messages, media, err := generateOffer()

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

	//TODO: Send SDP to application and then wait for answer response string message from application
	//sdpStr := getSDPStr(offerPc)
	reader := bufio.NewReader(os.Stdin)
	fmt.Printf("\nEnter Answer SDP:\n")
	answerStr, err := readSDP(reader)

	//TODO: Wait for peer SDP from application
	// answerStr, err := "", nil

	/*for {
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
	}*/

	if err != nil {
		return nil, nil, nil, nil, fmt.Errorf("Error reading answer: %s\n", err)
	}

	//apply answer to connection
	err = applyAnswer(offerPc, answerStr)
	if err != nil {
		return nil, nil, nil, nil, fmt.Errorf("Error applying answer: %s\n", err)
	}

	//Relay voice audio and the screen stream
	relayMediaOnceConnected(media, fb)

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

	// generateAnswer closes the connection itself if it fails
	answerPc, dcReady, dataChanOpen, messages, media, err := generateAnswer(SDPStr)
	if err != nil {
		return nil, nil, nil, nil, fmt.Errorf("Error generating answer: %s\n", err)
	}

	keepConnection := false
	defer func() {
		if !keepConnection {
			//If setup fails we may get an error on close, but we can ignore it
			_ = answerPc.Close()
		}
	}()

	fmt.Println("Answer SDP:")
	fmt.Println(getSDPStr(answerPc))

	//TODO: Send SDP to application

	//Relay voice audio and the screen stream, once. A second relay would add a second reader to the outgoing ring
	relayMediaOnceConnected(media, fb)

	keepConnection = true
	return answerPc, dcReady, dataChanOpen, messages, nil
}

// TODO: Move to helper file?
func goAndPrintIfError(f func() error) {
	go func() {
		err := f()
		if err != nil {
			fmt.Println("Error in concurrent func: " + err.Error())
		}
	}()
}
