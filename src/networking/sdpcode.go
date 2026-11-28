package main

import (
	"bufio"
	"bytes"
	"compress/flate"
	"encoding/base64"
	"errors"
	"fmt"
	"io"
	"strings"

	"github.com/pion/webrtc/v4"
)

// The raw SDP is a few KB of text. Deflate + URL-safe base64 turns it into a single-line
// string people can copy and paste. This is the "connection string" shown in the UI.

// Upper bound when decompressing, so a malicious string can't expand into gigabytes
const maxSDPBytes = 64 * 1024

func getSDPStr(pc *webrtc.PeerConnection) string {
	return pc.LocalDescription().SDP
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

func encodeSDP(sdp string) (string, error) {
	var buf bytes.Buffer
	w, err := flate.NewWriter(&buf, flate.BestCompression)
	if err != nil {
		return "", err
	}
	if _, err := w.Write([]byte(sdp)); err != nil {
		return "", err
	}
	if err := w.Close(); err != nil {
		return "", err
	}
	return base64.RawURLEncoding.EncodeToString(buf.Bytes()), nil
}

func decodeSDP(code string) (string, error) {
	code = strings.TrimSpace(code)
	if code == "" {
		return "", errors.New("connection string is empty")
	}

	raw, err := base64.RawURLEncoding.DecodeString(code)
	if err != nil {
		return "", fmt.Errorf("connection string is not valid: %w", err)
	}

	out, err := io.ReadAll(io.LimitReader(flate.NewReader(bytes.NewReader(raw)), maxSDPBytes+1))
	if err != nil {
		return "", fmt.Errorf("connection string is not valid: %w", err)
	}
	if len(out) > maxSDPBytes {
		return "", errors.New("connection string is too large")
	}
	if !strings.HasPrefix(string(out), "v=0") {
		return "", errors.New("connection string does not contain a session description")
	}
	return string(out), nil
}
