using System.ComponentModel;
using System.Threading.Channels;
using SGVS.Frontend.Services.Video;
using SharedMemLibrary;

namespace SGVS.Frontend.Services;

// the screen stream of NetworkMeetingService.
//   sending: a publisher (ffmpeg encoding the dummy clip for now) raises access units. each one goes to the
//     Video + Outgoing ring for the networking module to send, and to a loopback pipeline that decodes it
//     for your own tile
//   receiving: AttachRemoteVideo starts a pipeline on someone else's access units, which is how a live
//     stream from a peer plugs in
public sealed partial class NetworkMeetingService
{
    private IStreamPublisher? _publisher;
    private IncomingVideoPipeline? _selfVideo;
    private IncomingVideoPipeline? _remoteVideo;
    private ChannelWriter<byte[]>? _selfVideoWriter;

    // asked of shared memory once and then kept: the networking module's relay keeps waiting on this ring
    // across stops, so starting again just refills it
    private SharedMemLibrary.Encoding? _outgoingVideo;

    // false if it could not start, Error says why
    private bool StartVideo(VideoServices video)
    {
        IStreamPublisher publisher;
        try
        {
            publisher = video.CreatePublisher();
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or FileNotFoundException)
        {
            Error = "Could not start the stream: " + e.Message;
            return false;
        }

        SharedMemLibrary.Encoding outgoing;
        try
        {
            outgoing = _outgoingVideo ?? VideoEncodings.NewOutgoing();
            if (_outgoingVideo is null)
            {
                if (!_sharedMemory.RequestEncoding(outgoing))
                {
                    publisher.Dispose();
                    Error = "Could not start the stream: the shared memory video channel is unavailable.";
                    return false;
                }

                _outgoingVideo = outgoing;
            }
        }
        catch (Exception e) when (e is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            publisher.Dispose();
            Error = "Could not start the stream: " + e.Message;
            return false;
        }

        // a few frames of slack, and the oldest goes if the decoder falls behind
        var loopback = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(60)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        IVideoDecoder decoder;
        try
        {
            decoder = video.CreateDecoder();
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            publisher.Dispose();
            Error = "Could not start the stream: " + e.Message;
            return false;
        }

        _selfVideoWriter = loopback.Writer;
        _selfVideo = new IncomingVideoPipeline(new ChannelVideoSource(loopback.Reader), decoder,
            new ParticipantVideoSink(Self, _post, tracksActivity: false));
        _selfVideo.Start();

        publisher.AccessUnitEncoded += au => OnAccessUnitEncoded(outgoing, loopback.Writer, au);
        publisher.Failed += message => _post(() =>
        {
            StopStream();
            Error = message;
        });

        // set before Start so a failed start can be torn down with the rest
        _publisher = publisher;
        try
        {
            publisher.Start();
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // ffmpeg exists but cannot run (not an executable, wrong architecture)
            StopVideo();
            Error = "Could not start the stream: " + e.Message;
            return false;
        }

        return true;
    }

    private void OnAccessUnitEncoded(SharedMemLibrary.Encoding outgoing, ChannelWriter<byte[]> loopback, byte[] accessUnit)
    {
        try
        {
            // false means the ring is full: the frame is dropped, the next keyframe catches the watcher up
            _sharedMemory.PushBytes(outgoing, accessUnit);
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException)
        {
            // shared memory was closed under us while the app is shutting down
        }

        loopback.TryWrite(accessUnit);
    }

    private void StopVideo()
    {
        var publisher = _publisher;
        var writer = _selfVideoWriter;
        var pipeline = _selfVideo;
        _publisher = null;
        _selfVideoWriter = null;
        _selfVideo = null;

        publisher?.Dispose();
        writer?.TryComplete();
        pipeline?.Stop();
    }

    // ends the call on this side: stops your stream and the one you are watching. safe to call twice
    public void Dispose()
    {
        if (Self.IsStreamer) StopStream();
        else StopVideo();

        _remoteVideo?.Stop();
        _remoteVideo = null;
    }

    // someone else's stream. on the UI thread. a no-op when this service has no video services
    public void AttachRemoteVideo(IEncodedVideoSource source)
    {
        if (_video is null) return;

        IVideoDecoder decoder;
        try
        {
            decoder = _video.CreateDecoder();
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            Error = "Could not show the stream: " + e.Message;
            return;
        }

        var peer = EnsurePeer();
        _remoteVideo?.Stop();
        _remoteVideo = new IncomingVideoPipeline(source, decoder, new ParticipantVideoSink(peer, _post, tracksActivity: true));
        _remoteVideo.Start();
    }
}
