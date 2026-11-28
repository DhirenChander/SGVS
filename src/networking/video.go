package main

import (
	"fmt"
	"shared_mem_lib"
	"time"

	"github.com/pion/rtp/codecs"
	"github.com/pion/webrtc/v4"
	"github.com/pion/webrtc/v4/pkg/media"
	"github.com/pion/webrtc/v4/pkg/media/samplebuilder"
)

// The screen stream. Its track must be in the first offer/answer: connection strings are swapped by hand once
// and there is no renegotiation afterwards.
const screenVideoTrackID = "screen_video"
const videoFrameDuration = time.Second / 30
const videoBitrateKbps = 3000                   // matches VideoFormat.BitrateKbps in the frontend
const maxVideoFrameBytes = 500000               // one video slot in shared memory, bigger access units are dropped
const maxVideoLateness = 150 * time.Millisecond // how long an incomplete frame waits for a missing packet
const maxVideoPacketsLate = 256
const screenVideoFmtp = "level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f" // pion's default H.264 line

const defaultVideoWidth = 720
const defaultVideoHeight = 1280
const defaultVideoFrameRate = 30

const videoClockRate = 90000 //Standard for RTP video

// H.264 Access Unit Delimiter
// Allows the decoder to immediately release the frame
var AUDBytes = []byte{0, 0, 0, 1, 9, 0xF0}

// The screen stream track: H.264 at 90 kHz. Frames are written as whole access units (WriteSample) and pion
// splits them into RTP packets.
func AddScreenVideoTrack(pc *webrtc.PeerConnection) (*webrtc.TrackLocalStaticSample, error) {
	track, err := webrtc.NewTrackLocalStaticSample(
		webrtc.RTPCodecCapability{
			MimeType:    webrtc.MimeTypeH264,
			ClockRate:   videoClockRate,
			SDPFmtpLine: screenVideoFmtp,
		},
		screenVideoTrackID,
		screenVideoTrackID+"_stream",
	)
	if err != nil {
		return nil, err
	}

	sender, err := pc.AddTrack(track)
	if err != nil {
		return nil, err
	}

	// Drain RTCP the same way the voice track does, so receiver reports and NACKs don't back up
	go func() {
		buf := make([]byte, 1500)
		for {
			if _, _, err := sender.Read(buf); err != nil {
				return
			}
		}
	}()

	return track, nil
}

// Polls shared memory for a Video + Outgoing encoding (the frontend registers one the first time you stream) and
// starts a relay for each new one. Runs until the connection closes
// TODO: Change to wait for message that we are streaming?
// Eventually networking module will be in charge of managing encodings as well.
func watchForOutgoingVideoEncs(m *peerMedia, fb frameBuffer) {

	const checkForEncodingRepeatTime = 500 * time.Millisecond

	var relaying []shared_mem_lib.Encoding

	for {
		if encs, ok := fb.ReadEncodings(10); ok {
			fb.AlignRingsToConfig() //TODO: Ask Austin when this is needed
			for _, enc := range encs {
				if !enc.IsVideo() || !enc.IsOutgoing() || containsEncoding(relaying, enc) {
					continue
				}
				relaying = append(relaying, enc)
				goAndPrintIfError(func() error { return relayOutgoingVideoToRemote(m, fb, enc) })
			}
		}

		select {
		case <-m.Closed:
			return
		case <-time.After(checkForEncodingRepeatTime):
		}
	}
}

// Rebuilds access units from the remote's RTP packets and pushes each into a Video + Incoming ring, with a
// delimiter in front (see audBytes). Access units over one shared memory slot are dropped
func relayIncomingVideoToBuffer(track *webrtc.TrackRemote, fb frameBuffer) error {
	enc := shared_mem_lib.NewVideoEncoding(videoBitrateKbps, videoBitrateKbps, defaultVideoWidth, defaultVideoHeight, defaultVideoFrameRate)
	enc.SetIncoming()

	if !fb.RequestEncoding(enc) {
		return fmt.Errorf("Failed to request video encoding")
	}

	if !fb.AlignRingsToConfig() {
		return fmt.Errorf("Failed to align rings to config")
	}

	// A lost packet that NACK does not recover would otherwise hold every later frame until 256 more packets
	// arrive (~0.8 s at 3 Mbps). Give up on it after maxVideoLateness instead; the next keyframe repairs the picture

	sb := samplebuilder.New(maxVideoPacketsLate, &codecs.H264Packet{}, videoClockRate, samplebuilder.WithMaxTimeDelay(maxVideoLateness))

	for {
		packet, _, err := track.ReadRTP()
		if err != nil {
			return fmt.Errorf("Error reading incoming video: %w", err)
		}

		sb.Push(packet)
		//Reads all finished frames and pushes to buffer
		for sample := sb.Pop(); sample != nil; sample = sb.Pop() {
			frame := make([]byte, 0, len(AUDBytes)+len(sample.Data))
			frame = append(frame, AUDBytes...)
			frame = append(frame, sample.Data...)

			if len(frame) > maxVideoFrameBytes {
				fmt.Printf("Dropped a %d byte video frame, a slot holds %d\n", len(frame), maxVideoFrameBytes)
				continue
			}

			gcFrame := shared_mem_lib.NewGCFrameWithData(enc, frame)
			pushed := fb.PushFrames([]shared_mem_lib.GCFrame{gcFrame})
			gcFrame.Close()
			if !pushed {
				// Ring full, drop the frame. The next keyframe will fix the video
				fmt.Println("Video ring full, dropped a frame")
			}
		}
	}
}

// Pops one access unit at a time off the encoding's ring and sends it. One at a time because the wrapper pins a
// whole 500 KB buffer for every frame it is asked for
func relayOutgoingVideoToRemote(m *peerMedia, fb frameBuffer, enc shared_mem_lib.Encoding) error {
	const ringMissingTimeout = 50 * time.Millisecond
	const popFramesTimeOutMs = 10000
	const maxFramesPerPop = 1 //Change this to be higher?

	for {
		select {
		case <-m.Closed:
			return nil
		default:
		}

		ok, frames := fb.BlockingPopFrames(enc, maxFramesPerPop, popFramesTimeOutMs)
		if !ok {
			// Pop usually waits out its timeout, but fails instantly if the ring isn't there
			time.Sleep(ringMissingTimeout)
			continue
		}

		for i := range frames {
			data := make([]byte, len(frames[i].Bytes))
			copy(data, frames[i].Bytes)
			frames[i].Close()

			err := m.VideoTrack.WriteSample(media.Sample{Data: data, Duration: videoFrameDuration})
			if err != nil {
				return err
			}
		}
	}
}
