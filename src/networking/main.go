package main

import (
	"bufio"
	"fmt"
	"os"
	"time"

	"github.com/pion/webrtc/v4"
)

// TODO: only close peer connection if it is not nil (in all files)
func main() {

	args := os.Args[1:]

	//Check if user wants to create a connection or answer
	host := (len(args) >= 1 && args[0] == "host")

	pcChan := make(chan *webrtc.PeerConnection, 1)

	go func() {
		pcChan <- testCallAndResponse(host)
	}()

	<-time.After(5 * time.Minute)
	<-pcChan //Used to keep peer connection open during testing

}

func testCallAndResponse(host bool) *webrtc.PeerConnection {
	if host /* We are the host */ {

		peerConnection, dataChan, dataChanOpen, messages, err := setupPcAsHost()

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

		peerConnection, dcReady, dataChanOpen, messages, err := setupPcAsAttendee()

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

func setupPcAsHost() (
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

	//Send audio
	// err = selfVoiceTrack.WriteSample(media.Sample{})

	//Don't do this here
	//Return and then wait at a higher level after SDP printed
	remoteVoiceTrack := <-remoteVoiceTrackChan

	//Read incoming audio
	// Should do this in a gorouting + for loop
	remoteVoiceTrack.ReadRTP() //Blocking read
	// remoteVoiceTrack.Read()

	//Send data to processing module

	keepConnection = true
	return offerPc, dataChan, dataChanOpen, messages, nil
}

func setupPcAsAttendee() (
	*webrtc.PeerConnection,
	chan *webrtc.DataChannel,
	chan bool, //Signals when dataChannel is open (by closing itself)
	chan webrtc.DataChannelMessage, //Messages are sent here
	error) {

	reader := bufio.NewReader(os.Stdin)

	//Wait for user input
	fmt.Println("Enter connection string:\n")
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

	keepConnection = true
	return answerPc, dcReady, dataChanOpen, messages, nil
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
