package main

import (
	"bufio"
	"fmt"
	"os"
)

func main() {

	args := os.Args[1:]

	//Check if user wants to create a connection or answer
	makeAnswer := (len(args) >= 1 && args[0] == "answer")

	reader := bufio.NewReader(os.Stdin)

	//TODO: Move these into separate functions
	if makeAnswer {
		//wait for user input

		fmt.Println("Enter connection string:\n")
		SDPOfferStr, err := readSDP(reader)
		if err != nil {
			fmt.Printf("Error reading offer: %s\n", err)
			return
		}

		answerPc, err := generateAnswer(SDPOfferStr)
		defer func() { _ = answerPc.Close() }()
		if err != nil {
			fmt.Printf("Error generating answer: %s\n", err)
			return
		}

		fmt.Printf("Answer SDP:\n%s", getSDPStr(answerPc))

		//keep connection alive while waiting for waiting for message

		messages := make(chan []byte, 1)

		<-messages

		//close connection once finished

	} else {
		offerPc, err := generateOffer()
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

		//Temp for now
		// just wait forever for now to keep connection open
		// TODO: pass this channel into create offer and use
		// it to have messages recieve signals
		messages := make(chan []byte, 1)

		<-messages

		//close connection once finished

	}

}

func readSDP(reader *bufio.Reader) (string, error) {
	SDPStr := ""
	for {
		ln, err := reader.ReadString('\n')
		if err != nil {
			return SDPStr, err
		}
		if ln == "END\r\n" || ln == "END\n" {
			return SDPStr, nil
		}
		SDPStr += ln
	}
}
