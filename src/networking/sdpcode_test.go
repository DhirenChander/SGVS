package main

import (
	"strings"
	"testing"
)

func TestSDPCodeRoundTrip(t *testing.T) {
	sdp := "v=0\r\no=- 1 2 IN IP4 127.0.0.1\r\ns=-\r\n" + strings.Repeat("a=candidate:1 1 udp 1 10.0.0.1 5000 typ host\r\n", 20)

	code, err := encodeSDP(sdp)
	if err != nil {
		t.Fatal(err)
	}
	if strings.ContainsAny(code, " \r\n+/=") {
		t.Fatalf("code must be one line of URL-safe characters, got %q", code)
	}
	if len(code) >= len(sdp) {
		t.Fatalf("code (%d) should be shorter than the SDP (%d)", len(code), len(sdp))
	}

	got, err := decodeSDP("  " + code + "\n") // tolerate whitespace from copy/paste
	if err != nil {
		t.Fatal(err)
	}
	if got != sdp {
		t.Fatal("decoded SDP differs from the original")
	}
}

func TestDecodeSDPRejectsBadInput(t *testing.T) {
	notSDP, _ := encodeSDP("hello world")
	bomb, _ := encodeSDP("v=0" + strings.Repeat("x", maxSDPBytes+10))

	for name, in := range map[string]string{
		"empty":       "",
		"blank":       "   \n",
		"not base64":  "!!!not base64!!!",
		"not deflate": "aGVsbG8gd29ybGQ",
		"not an sdp":  notSDP,
		"too large":   bomb,
	} {
		if _, err := decodeSDP(in); err == nil {
			t.Errorf("%s: expected an error", name)
		}
	}
}
