package main

import (
	"fmt"
	"os"
)

func main() {

	args := os.Args[1:]

	//Check if user wants to create a connection or answer
	answer := false

	//TODO: Rewrite to reduce nesting
	if answer {
		//wait for user input
		SPDOfferStr := "place holder"
		answerPc, err := generateAnswer(SPDOfferStr)
		if err != nil {
			fmt.Printf("Error generating answer: %s\n", err)
		} else {
			fmt.Printf("Answer SDP:\n%s", getSDPStr(answerPc))

			//keep connection alive while waiting for waiting for message
			//close connection once finished
		}

	} else {
		offerPc, err := generateOffer()
		if err != nil {
			fmt.Printf("Error generating offer: %s\n", err)
		} else {
			fmt.Printf("Offer SDP:\n%s", getSDPStr(offerPc))

			//Prompt user for answer string and wait

			//apply answer to connection

			//establish connection

			//close connection once finished
		}
	}

	fmt.Println("Hello, World!")

	generateOffer()
}
