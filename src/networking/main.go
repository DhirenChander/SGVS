package main

import (
	"bufio"
	"fmt"
	"os"

	"github.com/pion/webrtc/v4"
)

func main() {

	args := os.Args[1:]

	//Check if user wants to create a connection or answer
	host := (len(args) >= 1 && args[0] == "host")

	go testCallAndResponse(host)
}

func testCallAndResponse(host bool) {
	if host /* We are the host */ {

		peerConnection, dataChan, dataChanOpen, messages, err := setupPcAsHost()

		if err != nil {
			fmt.Printf("Error setting up connection: %s\n", err)
			return
		}

		//Wait for data channel to open
		<-dataChanOpen

		dataChan.SendText("Hello from host :)")

		//Wait for response
		response := <-messages

		if !response.IsString {
			fmt.Printf("Message was not a string")
			return
		}

		fmt.Printf("Recieved Message: %s\n", string(response.Data))

		_ = peerConnection.Close()

	} else /* We are not the host */ {

		peerConnection, dataChan, dataChanOpen, messages, err := setupPcAsAttendee()

		if err != nil {
			fmt.Printf("Error setting up connection: %s\n", err)
			return
		}

		<-dataChanOpen

		firstMessage := <-messages

		if !firstMessage.IsString {
			fmt.Printf("Message was not a string")
			return
		}

		fmt.Printf("Recieved Message: %s\n", string(firstMessage.Data))

		fmt.Println("Sending reponse")

		dataChan.SendText("Hello from the other side")

		_ = peerConnection.Close()

	}
}

func setupPcAsHost() (
	*webrtc.PeerConnection,
	*webrtc.DataChannel,
	chan struct{}, //Signals when dataChannel is open (by closing itself)
	chan webrtc.DataChannelMessage, //Messages are sent here
	error) {

	// Change to take in a reader and writer if using a different IO stream
	reader := bufio.NewReader(os.Stdin)

	offerPc, dataChan, dataChanOpen, messages, err := generateOffer()

	keepConnection := false
	defer func() {
		if !keepConnection {
			//If setup fails we may get an error on close, but we can ignore it
			_ = offerPc.Close()
		}
	}()

	if err != nil {
		return nil, nil, nil, nil, fmt.Errorf("Error generating offer: %s\n", err)
	}

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

	keepConnection = true
	return offerPc, dataChan, dataChanOpen, messages, nil
}

func setupPcAsAttendee() (
	*webrtc.PeerConnection,
	*webrtc.DataChannel,
	chan struct{}, //Signals when dataChannel is open (by closing itself)
	chan webrtc.DataChannelMessage, //Messages are sent here
	error) {

	reader := bufio.NewReader(os.Stdin)

	//Wait for user input
	fmt.Println("Enter connection string:\n")
	SDPOfferStr, err := readSDP(reader)
	if err != nil {
		return nil, nil, nil, nil, fmt.Errorf("Error reading offer: %s\n", err)
	}

	answerPc, dc, dataChanOpen, messages, err := generateAnswer(SDPOfferStr)

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

	return answerPc, dc, dataChanOpen, messages, nil
}

func sendTestMessage() {

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
