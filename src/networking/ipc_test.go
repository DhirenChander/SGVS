package main

import (
	"bufio"
	"encoding/json"
	"io"
	"strings"
	"testing"
	"time"
)

// one side of an ipc conversation: write commands, read events
type ipcClient struct {
	t      *testing.T
	stdin  io.WriteCloser
	events chan ipcEvent
	done   chan struct{}
}

func startIPC(t *testing.T) *ipcClient {
	t.Helper()
	inR, inW := io.Pipe()
	outR, outW := io.Pipe()

	c := &ipcClient{t: t, stdin: inW, events: make(chan ipcEvent, 64), done: make(chan struct{})}

	go func() {
		runIPC(inR, outW)
		outW.Close()
		close(c.done)
	}()
	go func() {
		sc := bufio.NewScanner(outR)
		sc.Buffer(make([]byte, 0, 64*1024), 1024*1024)
		for sc.Scan() {
			var e ipcEvent
			if err := json.Unmarshal(sc.Bytes(), &e); err != nil {
				t.Errorf("stdout line is not a JSON event: %q", sc.Text())
				continue
			}
			c.events <- e
		}
		close(c.events)
	}()
	t.Cleanup(func() { inW.Close() })
	return c
}

func (c *ipcClient) send(cmd ipcCommand) {
	c.t.Helper()
	b, _ := json.Marshal(cmd)
	if _, err := c.stdin.Write(append(b, '\n')); err != nil {
		c.t.Fatal(err)
	}
}

// waits for an event of this kind, skipping others (state changes arrive in between)
func (c *ipcClient) expect(kind string) ipcEvent {
	c.t.Helper()
	timeout := time.After(20 * time.Second)
	for {
		select {
		case e, ok := <-c.events:
			if !ok {
				c.t.Fatalf("event stream closed while waiting for %q", kind)
			}
			if e.Event == kind {
				return e
			}
		case <-timeout:
			c.t.Fatalf("timed out waiting for %q", kind)
		}
	}
}

func (c *ipcClient) expectState(state string) {
	c.t.Helper()
	timeout := time.After(20 * time.Second)
	for {
		select {
		case e := <-c.events:
			if e.Event == "state" && e.State == state {
				return
			}
		case <-timeout:
			c.t.Fatalf("timed out waiting for state %q", state)
		}
	}
}

func connect(t *testing.T) (host, guest *ipcClient) {
	t.Helper()
	host, guest = startIPC(t), startIPC(t)

	host.send(ipcCommand{Cmd: "host", Name: "Hazel"})
	offer := host.expect("offer")
	if offer.Code == "" {
		t.Fatal("offer has no code")
	}

	guest.send(ipcCommand{Cmd: "join", Name: "Gus", Code: offer.Code})
	answer := guest.expect("answer")

	host.send(ipcCommand{Cmd: "accept", Code: answer.Code})
	host.expectState("connected")
	guest.expectState("connected")
	return host, guest
}

func TestIPCHostJoinExchangeNamesAndStream(t *testing.T) {
	host, guest := connect(t)

	if got := host.expect("peer").Name; got != "Gus" {
		t.Errorf("host saw peer %q, want Gus", got)
	}
	if got := guest.expect("peer").Name; got != "Hazel" {
		t.Errorf("guest saw peer %q, want Hazel", got)
	}

	host.send(ipcCommand{Cmd: "stream", On: true})
	if e := guest.expect("peer_stream"); e.On == nil || !*e.On {
		t.Errorf("guest should see the host streaming, got %+v", e)
	}
	host.send(ipcCommand{Cmd: "stream", On: false})
	if e := guest.expect("peer_stream"); e.On == nil || *e.On {
		t.Errorf("guest should see the stream stop, got %+v", e)
	}
	guest.send(ipcCommand{Cmd: "stream", On: true})
	if e := host.expect("peer_stream"); e.On == nil || !*e.On {
		t.Errorf("host should see the guest streaming, got %+v", e)
	}
}

func TestIPCLeaveTellsOtherSideAndAllowsNewCall(t *testing.T) {
	host, guest := connect(t)

	guest.send(ipcCommand{Cmd: "leave"})
	guest.expect("left")
	host.expect("peer_left")

	// the process can host again after leaving
	guest.send(ipcCommand{Cmd: "host", Name: "Gus"})
	guest.expect("offer")
}

func TestIPCStdinClosedEndsCleanly(t *testing.T) {
	c := startIPC(t)
	c.send(ipcCommand{Cmd: "host", Name: "Hazel"})
	c.expect("offer")

	c.stdin.Close()
	select {
	case <-c.done:
	case <-time.After(10 * time.Second):
		t.Fatal("runIPC did not return after stdin closed")
	}
}

func TestIPCErrors(t *testing.T) {
	c := startIPC(t)

	for name, line := range map[string]string{
		"not json":        "this is not json",
		"unknown command": `{"cmd":"explode"}`,
		"accept no call":  `{"cmd":"accept","code":"abc"}`,
		"stream no call":  `{"cmd":"stream","on":true}`,
		"join garbage":    `{"cmd":"join","name":"x","code":"garbage"}`,
	} {
		if _, err := io.WriteString(c.stdin, line+"\n"); err != nil {
			t.Fatal(err)
		}
		if e := c.expect("error"); e.Message == "" {
			t.Errorf("%s: error event has no message", name)
		}
	}

	// still alive and usable afterwards
	c.send(ipcCommand{Cmd: "host", Name: "Hazel"})
	c.expect("offer")

	// second host while in a call is refused
	c.send(ipcCommand{Cmd: "host", Name: "Hazel"})
	if e := c.expect("error"); !strings.Contains(e.Message, "already") {
		t.Errorf("want an 'already in a call' error, got %q", e.Message)
	}
}

func TestIPCAcceptGarbageReply(t *testing.T) {
	c := startIPC(t)
	c.send(ipcCommand{Cmd: "host", Name: "Hazel"})
	c.expect("offer")
	c.send(ipcCommand{Cmd: "accept", Code: "garbage"})
	c.expect("error")
}
