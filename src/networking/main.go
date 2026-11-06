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
	makeAnswer := (len(args) >= 1 && args[0] == "answer")

	reader := bufio.NewReader(os.Stdin)

	//TODO: Move these into separate functions
	if makeAnswer {
		messages := make(chan webrtc.DataChannelMessage, 1)
		//wait for user input

		fmt.Println("Enter connection string:\n")
		SDPOfferStr, err := readSDP(reader)
		if err != nil {
			fmt.Printf("Error reading offer: %s\n", err)
			return
		}

		answerPc, err := generateAnswer(SDPOfferStr, messages)
		defer func() { _ = answerPc.Close() }()
		if err != nil {
			fmt.Printf("Error generating answer: %s\n", err)
			return
		}

		fmt.Printf("Answer SDP:\n%s", getSDPStr(answerPc))

		//keep connection alive while waiting for waiting for message

		firstMessage := <-messages

		// if !firstMessage.IsString {
		// 	fmt.Printf("Message was not a string")
		// }

		fmt.Printf("Recieved Message: %s\n", string(firstMessage.Data))

		// fmt.Printf("Sending response\n")
		// We need access to the data channel to send a response
		// Should the connection creation function return a function to send messages?

		//close connection once finished

	} else {
		messages := make(chan webrtc.DataChannelMessage, 1)
		offerPc, err := generateOffer(messages)
		defer func() { _ = offerPc.Close() }()
		if err != nil {
			fmt.Printf("Error generating offer: %s\n", err)

			return
		}

		fmt.Printf("Offer SDP:\n%s", getSDPStr(offerPc))

		//Prompt user for answer string and wait
		fmt.Printf("\nEnter Answer SDP:\n")
		answerStr, err := readSDP(reader)
		if err != nil {
			fmt.Printf("Error reading answer: %s\n", err)
			return
		}

		//apply answer to connection
		err = applyAnswer(offerPc, answerStr)
		if err != nil {
			fmt.Printf("Error applying answer: %s\n", err)
			return
		}

		//send data over connection

		//add needed audio/video streams

		firstResponse := <-messages

		fmt.Printf("Recieved Message: %s\n", string(firstResponse.Data))

		//close connection once finished

	}

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
