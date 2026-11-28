package main

import (
	"errors"
	"fmt"
	"shared_mem_lib"
	"time"

	"github.com/pion/interceptor/pkg/jitterbuffer"
	"github.com/pion/webrtc/v4"
	"github.com/pion/webrtc/v4/pkg/media"
)

const voiceAudioTrackId = "voice_audio"

var defaultAudioCapability = webrtc.RTPCodecCapability{
	MimeType:  webrtc.MimeTypeOpus,
	ClockRate: 48000,
	Channels:  2,
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

	return shared_mem_lib.Encoding{}, fmt.Errorf("Did not find audio encoding after %s", timeOut.String())
}

func AddVoiceTrack(pc *webrtc.PeerConnection) (
	*webrtc.TrackLocalStaticSample,
	*webrtc.RTPSender,
	error,
) {
	track, err := webrtc.NewTrackLocalStaticSample(
		defaultAudioCapability,
		voiceAudioTrackId,
		voiceAudioTrackId+"_stream",
	)
	if err != nil {
		return nil, nil, err
	}

	sender, err := pc.AddTrack(track)
	if err != nil {
		return nil, nil, err
	}

	// Drain the incoming RTCP data until it errors
	// 		For now just discard the data,
	// not the audio data, only data about connection stability and performance
	// Use this to read Receiver Reports and updates on network conditions
	go func() {
		buf := make([]byte, 1500)
		for {
			//Use ReadRTCP later to get formatted info
			if _, _, err := sender.Read(buf); err != nil {
				return
			}
		}
	}()

	return track, sender, nil
}

func relayIncomingAudioToBuffer(track *webrtc.TrackRemote, fb frameBuffer) error {

	//Playback will pause and wait if under minimum in buffer
	//Or skip over packets if over maximum
	const minimumBufferedPackets = 4 // -> 40ms delay
	const audioFrameDuration = 10 * time.Millisecond
	const maxBufferedPackets = 7 // -> 80ms
	const resetSize = maxBufferedPackets

	enc := shared_mem_lib.NewAudioEncoding(true) // Is_input is not important here
	enc.SetIncoming()

	if !fb.RequestEncoding(enc) {
		return fmt.Errorf("Failed to request encoding")
	}

	if !fb.AlignRingsToConfig() {
		return fmt.Errorf("Failed to align rings to config")
	}

	opts := []jitterbuffer.Option{
		jitterbuffer.WithMinimumPacketCount(minimumBufferedPackets),
		jitterbuffer.WithOverflowPacketCount(maxBufferedPackets),
	}

	jb := jitterbuffer.New(opts...)

	readDone := make(chan struct{}) //Will stop playing from jitter buffer when this closes

	go func() {
		defer close(readDone)
		firstPacket := true
		for {
			packet, _, err := track.ReadRTP()
			if err != nil {
				fmt.Println("Error reading incoming audio")
				return
			}
			fmt.Printf("Received audio packet from remote:\n\tSequence: %d\n", packet.SequenceNumber)

			sequenceNum := packet.Header.SequenceNumber
			if int16(sequenceNum-jb.PlayoutHead()) < 0 && !firstPacket {
				//We have already played past this packet, arrived too late
				//Don't push packet
				continue

			}
			jb.DiscardIfOverflow(resetSize)
			jb.Push(packet) //First push sets playout head
			firstPacket = false
		}
	}()

	go func() {
		ticker := time.NewTicker(audioFrameDuration)
		defer ticker.Stop()

		for {
			select {
			case <-readDone:
				return
			case <-ticker.C:
				packet, err := jb.Pop()
				if errors.Is(err, jitterbuffer.ErrNotFound) {
					//Next packet not yet received (but more are queued after it)
					jb.SetPlayoutHead(jb.PlayoutHead() + 1)
					continue
				}
				if errors.Is(err, jitterbuffer.ErrInvalidOperation) {
					//Queue empty
					continue
				}
				if errors.Is(err, jitterbuffer.ErrPopWhileBuffering) {
					//Buffer waiting until more data arrives
					continue
				}
				if err != nil {
					fmt.Printf("Error reading from audio jitter buffer: %v\n", err)
					return
				}

				gcFrame := shared_mem_lib.NewGCFrame(enc)
				gcFrame.SetData(packet.Payload)
				if !fb.PushFrames([]shared_mem_lib.GCFrame{gcFrame}) {
					fmt.Println("Error pushing audio frame to shared buffer")
					gcFrame.Close()
					return
				}
				gcFrame.Close()
			}
		}
	}()

	return nil
}

// Add way to stop sending frames?
// Add a channel to signal something has failed?
func relayOutgoingAudioToRemote(track *webrtc.TrackLocalStaticSample, fb frameBuffer, enc shared_mem_lib.Encoding) error {
	const timeOutMs = 100000
	const maxAudioFrames = 100 // 10ms per frame -> 1 seconds

	//Hard code for now
	const audioFrameDuration = 10 * time.Millisecond

	go func() {
		var err error
		var ok bool
		for err == nil {
			ok, err = popAndCopyAudioFrames(track, fb, enc, maxAudioFrames, timeOutMs, audioFrameDuration)
			if !ok {
				<-time.After(audioFrameDuration / 2)
			}
		}

		//Will have error if we exited the loop
		fmt.Println("Error relaying outgoing audio: " + err.Error())
	}()

	return nil
}

func popAndCopyAudioFrames(
	track *webrtc.TrackLocalStaticSample, fb frameBuffer, enc shared_mem_lib.Encoding,
	maxAudioFrames uint32, timeOutMs uint32, audioFrameDuration time.Duration) (bool, error) {

	var framesToSend []shared_mem_lib.GCFrame
	ok, framesToSend := fb.BlockingPopFrames(enc, maxAudioFrames, timeOutMs) //BlockingPopFrames resizes slice to only be as long as needed

	if !ok {
		return false, nil
	}

	//Default value of bool is false, no need to initialize
	var closed = make([]bool, len(framesToSend))
	defer closeAll(framesToSend, closed)

	// fmt.Println("Received frames to send from processing module")

	var err error
	for i := 0; i < len(framesToSend) && err == nil; i++ {

		framesCopy := make([]byte, len(framesToSend[i].Bytes))
		copy(framesCopy, framesToSend[i].Bytes)
		framesToSend[i].Close()
		closed[i] = true

		var media media.Sample = media.Sample{
			Data:     framesCopy,
			Duration: audioFrameDuration,
		}
		err = track.WriteSample(media)
	}

	return true, err
}

func closeAll(frames []shared_mem_lib.GCFrame, closed []bool) {
	for i, fr := range frames {
		if !closed[i] {
			fr.Close()
		}
	}
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
