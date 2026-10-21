# Netowrking Module Documentation

## Architecture

### Core components

- Networking layer to send data
- Management layer (if needed)
- Algorithm module to route data

### Algorithm Module

- Acts as source of truth for active connections
- Constantly query the WebRTC library to determine current network speeds
- Requests encodings
  Create new threads for this

### Monitoring (If RTCP not usable or not sufficient)

1. Once a frame is sent to application, record percent of packets received and share to sender
2. Similar for audio (decide on how many chunks of audio to receive before sending

## Sequences

### Sending Sequence (Single Encoding) (1 thread per encoding?)

1. Poll algorithm to determine which frames should be sent where
2. If the active encoding is not requested, stop
3. Read all of the frames from memory from all of the requested encodings
4. Send packets in frame order based on the algorithms selections (Spread out over time to match bitrate if needed)

### Joining Sequence (Client Side)

1. Send join request to host
2. Receive info about call

### Hosting Call

1. Listen for join requests
2. Send join request info to application

### Receiving Sequence (1 thread)

1. Packets received from network
2. Handle join requests separately
3. Placed into rolling array buffer
4. Send received data to the application module
   - Video: After short delay entire frame sent to application
   - Audio: After short delay, next chunk of audio sent to application
