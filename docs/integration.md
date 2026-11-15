# Frontend / backend integration

This describes how the Avalonia frontend is wired to the Go networking module and the shared memory
library, how a call gets set up, and how to build and test all of it.

Audio and video capture (`src/processing`) is **not** connected yet, see [Not done yet](#not-done-yet).

## The pieces

```
 +------------------------------+          stdin/stdout, one JSON object per line
 |  SGVS.Frontend (C#, Avalonia) | <----------------------------------------------+
 |                              |                                                 |
 |  NetworkMeetingService       |        +----------------------------------+     |
 |    NetworkingProcess  -------+------> |  sgvs-networking ipc (Go, pion)  | ----+----> other peer
 |    SharedMemoryBridge        |        +----------------------------------+   WebRTC data channel
 +--------------+---------------+
                |
                |  named Windows shared memory (shared_mem_lib)
                v
        processing / networking read the same config
```

| Piece | Where | Job |
|---|---|---|
| UI pages | `src/SGVS.Frontend/Views`, `ViewModels` | Start, Meeting and Watch screens |
| `IMeetingService` | `Services/IMeetingService.cs` | What the UI needs from a call. Two implementations below |
| `FakeMeetingService` | `Services/FakeMeetingService.cs` | Pretend call with the people from the Figma mockup. Used by the XAML previewer and when `SGVS_FAKE=1` |
| `NetworkMeetingService` | `Services/NetworkMeetingService.cs` | The real call. Turns networking events into `Members`, `State` and `Error` |
| `NetworkingProcess` | `Services/Networking/NetworkingProcess.cs` | Starts `backend/sgvs-networking` as a child process and reads/writes its lines |
| `NetworkingMessages` | `Services/Networking/NetworkingMessages.cs` | The JSON commands and events (the C# half of the protocol) |
| `SharedMemoryBridge` | `Services/SharedMemory/ISharedMemoryBridge.cs` | The frontend's handle on the shared memory config (`sharing_screen` flag for now) |
| `ipc.go` | `src/networking/ipc.go` | The Go half of the protocol, started with `networking ipc` |
| `sdpcode.go` | `src/networking/sdpcode.go` | Turns the WebRTC session description into a pasteable string |
| shared memory library | `src/shared_mem_lib` | C core + C#/C++/Go wrappers, taken from `feat/shared-mem-client` |

## How a call is set up

There is no signalling server, so the two people swap connection strings by hand (chat, email, anything).
A call is one host and one guest, because `networking.go` only creates a single peer connection.

```
 Host app                      Host networking        Guest networking          Guest app
    |  host{name}  ------------>|                          |                       |
    |<------------ offer{code}  |                          |                       |
    |  (host copies the code and sends it to the guest)    |                       |
    |                           |                          |<---- join{name,code} -|
    |                           |                          |-----> answer{code} -->|
    |  (guest copies the reply and sends it to the host)   |                       |
    |  accept{code} ----------->|                          |                       |
    |                           |<======= WebRTC connects =======>|                |
    |<-- state{connected} ------|                          |-- state{connected} -->|
    |<-- peer{name: guest} -----|   (both sides send a     |-- peer{name: host} -->|
    |                           |    hello over the data   |                       |
    |                           |    channel once it opens)|                       |
```

In the UI:

1. Host presses **New meeting**. The Meeting page opens with a "waiting for a guest" box.
2. Host clicks the key icon at the bottom right, then the code, to copy the connection string.
3. Guest pastes it on the Start page and presses the arrow. The page now shows the reply and a **Copy reply** button.
4. Host pastes the reply into the box and presses **Connect**. Both apps move to the two-person gallery.

Starting or stopping a stream sets the `sharing_screen` flag in shared memory and sends a `stream` command so the
other side's tile for you switches to its streaming look. If you start streaming before the other person has
connected, it is announced as soon as the connection comes up.

When the other person leaves (or the connection fails) the call is over: the Meeting page says so and **Leave**
goes back to the Start page. Since the module only keeps one peer connection, you start a new meeting to
call again.

## Connection strings

`sdpcode.go` deflates the SDP and encodes it as URL-safe base64 without padding. That gives one line of about 700
characters instead of several KB of multi-line text. `decodeSDP` trims whitespace (pasting adds some), rejects
anything that doesn't decode to something starting with `v=0`, and caps the decompressed size at 64 KB.

The code rail on the Meeting page only previews the first six characters. Clicking it copies the whole string.

## The line protocol

Every message is one JSON object on one line. Commands go to the process's stdin, events come back on stdout.
Logging from the networking code goes to stderr (`logOut` in `networking.go`), so stdout is only ever protocol.

Commands, frontend to networking:

| `cmd` | Fields | Effect |
|---|---|---|
| `host` | `name` | Create an offer. Answered by `offer` |
| `join` | `name`, `code` | Create an answer from the host's code. Answered by `answer` |
| `accept` | `code` | Apply the guest's reply (host only) |
| `stream` | `on` | Tell the peer we started or stopped sharing |
| `leave` | | Close the connection. Answered by `left`. The process can host again afterwards |

Events, networking to frontend:

| `event` | Fields | Meaning |
|---|---|---|
| `offer` | `code` | Connection string to give to the guest |
| `answer` | `code` | Reply to give to the host |
| `state` | `state` | WebRTC connection state: `connecting`, `connected`, `disconnected`, `failed`, `closed` |
| `peer` | `name` | The other person introduced themselves |
| `peer_stream` | `on` | The other person started or stopped sharing |
| `peer_left` | | The other person's connection went away |
| `left` | | Our own `leave` finished |
| `error` | `message` | A command failed. The process keeps running |

Between the two peers the same data channel (`transceiver_management`) carries small `{"t":"hello"|"stream"}`
messages. Closing stdin makes `runIPC` close any open call and return, so the process never outlives the app.

If you change a field here, change `ipc.go` and `NetworkingMessages.cs` together.

## Shared memory

`src/shared_mem_lib` is the team's library (named file mappings and events on Windows). From the frontend it's used
through `GCFrameBuffer` in `csharp/lib.cs`.

`SharedMemoryBridge` opens the frame buffer the first time you start a stream, not at startup, so the app runs on a
machine without the native library. If opening fails, starting the stream reports an error instead of crashing.
On non-Windows platforms `NullSharedMemoryBridge` just remembers the flag, which lets the UI run on a Mac.

Changes made to the wrapper:
- `csharp.csproj` defines `WINDOWS` when building on Windows. `FrameBuffer` in `lib.cs` only has the event
  handle fields under that define, to match the `#ifdef _WIN32` in the C struct.
- `lib.cs` got the config functions that were declared in `framebuff.h` but had no wrapper:
  `set_share_video`, `get_sharing_video`, `set_front_end_encoding` and `get_front_end_encoding`.

One thing to watch: the C struct holds `Config *config` while the C# `FrameBuffer` embeds `Config` inline. It works
today because the struct is only ever passed by reference to native code, but the layouts do not match.

## Startup, shutdown and where things live

- `MainViewModel` makes a new `IMeetingService` each time the Start page is shown. Leaving a meeting, cancelling a
  join, or closing the window calls `LeaveAsync`, which kills the child process.
- `App.axaml.cs` calls `MainViewModel.Shutdown()` when the app is asked to exit.
- `SGVS.Frontend.csproj` has a `BuildNetworking` target that runs `go build` into `bin/<config>/net10.0/backend/`
  after every build. Without Go on PATH the build still succeeds with a warning, and hosting or joining then
  reports that the networking module is missing. `-p:SkipNetworkingBuild=true` skips it.
- Display names aren't decided yet, so `MainViewModel` uses the signed-in OS username.

## Running it

```
dotnet run --project src/SGVS.Frontend
```

Start two copies to try a call on one machine. `SGVS_FAKE=1` runs the pretend call instead.

## Tests

| Suite | Command | Covers |
|---|---|---|
| Go | `cd src/networking && go test -race ./...` | SDP codec, and `runIPC` host/join/accept/stream/leave between two in-memory peers |
| .NET | `dotnet test SGVS.slnx` | Protocol JSON, `NetworkMeetingService` against a fake process, view models, headless rendering of the real window, and a real-process suite that builds the Go module and connects two services |
| Windows only | `powershell -File scripts\test-windows.ps1` | Everything above plus the shared memory C tests, the processing gtest suite, and the native shared memory tests that are skipped elsewhere |

The real-process tests build `src/networking` into a temp folder and skip themselves if Go isn't installed.

## Not done yet

- Audio and video: `src/processing` is not started by the frontend and nothing plays incoming audio. The audio
  relay between networking and the shared buffer exists on `feat/054-integrate-processing-and-networking` and was
  not merged here.
- Video: starting a stream only flips the shared flag and tells the peer. There is no capture and no frame display.
  The screen/app choice in the Start stream dialog is not passed on yet.
- More than two people in a call.
- The processing module does not compile on macOS (`audio_capture.cpp` uses a Windows-only member in
  `get_buffered_frame`, and `audio_capture_test.cpp` uses `SILENT_FLAG` outside its `#ifdef _WIN32`).
- The Watch page's code rail still shows the guest's reply code.
