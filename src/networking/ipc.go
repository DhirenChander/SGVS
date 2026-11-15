package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"io"
	"sync"

	"github.com/pion/webrtc/v4"
)

// IPC mode: the frontend starts this program as `networking ipc` and talks to it with one JSON
// object per line. Commands come in on stdin, events go out on stdout, logs go to stderr.
//
//	commands:  host{name}  join{name,code}  accept{code}  stream{on}  leave
//	events:    offer{code}  answer{code}  state{state}  peer{name}  peer_stream{on}  peer_left  left  error{message}
//
// A call is one host and one attendee. Connection strings (code) are encoded SDP, see sdpcode.go.

type ipcCommand struct {
	Cmd  string `json:"cmd"`
	Name string `json:"name,omitempty"`
	Code string `json:"code,omitempty"`
	On   bool   `json:"on,omitempty"`
}

type ipcEvent struct {
	Event   string `json:"event"`
	Code    string `json:"code,omitempty"`
	State   string `json:"state,omitempty"`
	Name    string `json:"name,omitempty"`
	Message string `json:"message,omitempty"`
	On      *bool  `json:"on,omitempty"`
}

// what the two peers send each other over the data channel
type peerMessage struct {
	Type string `json:"t"`
	Name string `json:"name,omitempty"`
	On   bool   `json:"on,omitempty"`
}

const (
	peerHello  = "hello"
	peerStream = "stream"
)

type eventWriter struct {
	mu  sync.Mutex
	enc *json.Encoder
}

func (w *eventWriter) send(e ipcEvent) {
	w.mu.Lock()
	defer w.mu.Unlock()
	_ = w.enc.Encode(e) // Encode ends each object with a newline
}

func (w *eventWriter) sendError(format string, a ...any) {
	w.send(ipcEvent{Event: "error", Message: fmt.Sprintf(format, a...)})
}

// everything belonging to the call that is currently set up
type call struct {
	pc       *webrtc.PeerConnection
	name     string
	done     chan struct{} // closed when the call ends
	doneOnce sync.Once

	mu sync.Mutex
	dc *webrtc.DataChannel // nil until the data channel exists
}

func (c *call) end() {
	c.doneOnce.Do(func() {
		close(c.done)
		_ = c.pc.Close()
	})
}

func (c *call) setChannel(dc *webrtc.DataChannel) {
	c.mu.Lock()
	c.dc = dc
	c.mu.Unlock()
}

func (c *call) sendToPeer(m peerMessage) error {
	c.mu.Lock()
	dc := c.dc
	c.mu.Unlock()

	if dc == nil || dc.ReadyState() != webrtc.DataChannelStateOpen {
		return fmt.Errorf("not connected to a peer yet")
	}
	b, _ := json.Marshal(m)
	return dc.SendText(string(b))
}

// runIPC serves commands until in closes. Always returns after cleaning up any active call.
func runIPC(in io.Reader, out io.Writer) {
	events := &eventWriter{enc: json.NewEncoder(out)}
	var cur *call

	endCall := func() {
		if cur != nil {
			cur.end()
			cur = nil
		}
	}
	defer endCall()

	scanner := bufio.NewScanner(in)
	scanner.Buffer(make([]byte, 0, 64*1024), 1024*1024) // connection strings are long single lines

	for scanner.Scan() {
		line := scanner.Bytes()
		if len(line) == 0 {
			continue
		}

		var cmd ipcCommand
		if err := json.Unmarshal(line, &cmd); err != nil {
			events.sendError("could not read command: %v", err)
			continue
		}

		switch cmd.Cmd {
		case "host":
			if cur != nil {
				events.sendError("already in a call")
				continue
			}
			c, err := startHost(cmd.Name, events)
			if err != nil {
				events.sendError("could not host: %v", err)
				continue
			}
			cur = c

		case "join":
			if cur != nil {
				events.sendError("already in a call")
				continue
			}
			c, err := startJoin(cmd.Name, cmd.Code, events)
			if err != nil {
				events.sendError("could not join: %v", err)
				continue
			}
			cur = c

		case "accept":
			if cur == nil {
				events.sendError("not hosting a call")
				continue
			}
			sdp, err := decodeSDP(cmd.Code)
			if err == nil {
				err = applyAnswer(cur.pc, sdp)
			}
			if err != nil {
				events.sendError("could not accept reply: %v", err)
			}

		case "stream":
			if cur == nil {
				events.sendError("not in a call")
				continue
			}
			// With nobody connected yet there is nobody to tell, so this is not an error worth showing
			_ = cur.sendToPeer(peerMessage{Type: peerStream, On: cmd.On})

		case "leave":
			endCall()
			events.send(ipcEvent{Event: "left"})

		default:
			events.sendError("unknown command %q", cmd.Cmd)
		}
	}
}

func startHost(name string, events *eventWriter) (*call, error) {
	pc, dc, open, messages, err := generateOffer()
	if err != nil {
		return nil, err
	}

	code, err := encodeSDP(getSDPStr(pc))
	if err != nil {
		_ = pc.Close()
		return nil, err
	}

	c := &call{pc: pc, name: name, done: make(chan struct{}), dc: dc}
	watchConnection(c, events)
	events.send(ipcEvent{Event: "offer", Code: code})

	go c.talk(events, open, messages)
	return c, nil
}

func startJoin(name, code string, events *eventWriter) (*call, error) {
	sdp, err := decodeSDP(code)
	if err != nil {
		return nil, err
	}

	pc, dcReady, open, messages, err := generateAnswer(sdp)
	if err != nil {
		return nil, err
	}

	reply, err := encodeSDP(getSDPStr(pc))
	if err != nil {
		_ = pc.Close()
		return nil, err
	}

	c := &call{pc: pc, name: name, done: make(chan struct{})}
	watchConnection(c, events)
	events.send(ipcEvent{Event: "answer", Code: reply})

	go func() {
		select {
		case dc := <-dcReady:
			c.setChannel(dc)
			c.talk(events, open, messages)
		case <-c.done:
		}
	}()
	return c, nil
}

// reports connection state changes and works out when the other person is gone.
// replaces the handler generateOffer/generateAnswer installed, the log line is kept
func watchConnection(c *call, events *eventWriter) {
	var gone sync.Once
	c.pc.OnConnectionStateChange(func(s webrtc.PeerConnectionState) {
		fmt.Fprintf(logOut, "[ipc] Peer connection state: %s\n", s.String())
		events.send(ipcEvent{Event: "state", State: s.String()})

		if s == webrtc.PeerConnectionStateDisconnected ||
			s == webrtc.PeerConnectionStateFailed ||
			s == webrtc.PeerConnectionStateClosed {
			select {
			case <-c.done: // we hung up ourselves, nothing to report
			default:
				gone.Do(func() { events.send(ipcEvent{Event: "peer_left"}) })
			}
		}
	})
}

// introduces ourselves once the data channel is open, then relays what the peer says
func (c *call) talk(events *eventWriter, open chan struct{}, messages chan webrtc.DataChannelMessage) {
	select {
	case <-open:
	case <-c.done:
		return
	}

	if err := c.sendToPeer(peerMessage{Type: peerHello, Name: c.name}); err != nil {
		events.sendError("could not greet peer: %v", err)
	}

	for {
		select {
		case <-c.done:
			return
		case msg := <-messages:
			var m peerMessage
			if !msg.IsString || json.Unmarshal(msg.Data, &m) != nil {
				continue // ignore anything we don't understand
			}
			switch m.Type {
			case peerHello:
				events.send(ipcEvent{Event: "peer", Name: m.Name})
			case peerStream:
				on := m.On
				events.send(ipcEvent{Event: "peer_stream", On: &on})
			}
		}
	}
}
