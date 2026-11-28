using System.ComponentModel;
using System.Diagnostics;
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
    // long enough for ffmpeg to open its input and send the first frame, which takes well under a second
    private static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(5);

    // a remote decoder that dies is replaced this many times in a row before giving up
    private const int MaxDecoderRestarts = 3;

    // how many access units the loopback to your own tile holds
    private const int LoopbackCapacity = 60;

    private IStreamPublisher? _publisher;
    private IncomingVideoPipeline? _selfVideo;
    private IncomingVideoPipeline? _remoteVideo;
    private ChannelWriter<byte[]>? _selfVideoWriter;
    private Timer? _stallWatch;
    private long _lastAccessUnitAt; // Environment.TickCount64 of the last access unit, or of Start
    private int _remoteDecoderRestarts;

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
        IVideoDecoder decoder;
        try
        {
            outgoing = _outgoingVideo ??= RequestOutgoingVideo();
            decoder = video.CreateDecoder();
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            publisher.Dispose();
            Error = "Could not start the stream: " + e.Message;
            return false;
        }

        var loopback = StartSelfPreview(decoder);
        publisher.AccessUnitEncoded += au => OnAccessUnitEncoded(outgoing, loopback, au);
        publisher.Failed += message => _post(() => StopFailedStream(publisher, message));

        // set before Start so a failed start can be torn down with the rest
        _publisher = publisher;
        Volatile.Write(ref _lastAccessUnitAt, Environment.TickCount64);
        _stallWatch = WatchForStall(publisher, video.StallTimeout ?? DefaultStallTimeout);
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

    // a full ring is reported like the native failures, so StartVideo shows one message for both
    private SharedMemLibrary.Encoding RequestOutgoingVideo()
    {
        var outgoing = VideoEncodings.NewOutgoing();
        if (!_sharedMemory.RequestEncoding(outgoing))
            throw new InvalidOperationException("the shared memory video channel is unavailable.");
        return outgoing;
    }

    // your own tile only shows the first decoded frame as a faded still, so decoding stops after it.
    // returns where the publisher's access units go
    private ChannelWriter<byte[]> StartSelfPreview(IVideoDecoder decoder)
    {
        // a few frames of slack, and the oldest goes if the decoder falls behind
        var loopback = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(LoopbackCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        IncomingVideoPipeline? selfPipeline = null;
        ParticipantVideoSink? selfSink = null;
        _selfVideoWriter = loopback.Writer;
        selfSink = new ParticipantVideoSink(Self, _post, tracksActivity: false,
            onFirstFrame: () => _post(() => FreezeSelfPreview(selfPipeline!, selfSink!)));
        selfPipeline = new IncomingVideoPipeline(new ChannelVideoSource(loopback.Reader), decoder, selfSink);
        _selfVideo = selfPipeline;
        selfPipeline.Start();
        return loopback.Writer;
    }

    // UI thread. a failure posted by a publisher that has since been stopped or replaced is ignored
    private void StopFailedStream(IStreamPublisher publisher, string message)
    {
        if (_publisher != publisher) return;
        StopStream();
        Error = message;
    }

    // ffmpeg can hang without exiting (a capture source that stops delivering, a stuck pipe). nothing else would
    // notice: the watcher just sees the stream end. checks a few times per timeout, fails the stream once
    private Timer WatchForStall(IStreamPublisher publisher, TimeSpan timeout)
    {
        var reported = 0;
        var period = timeout / 4;
        return new Timer(_ =>
        {
            var quiet = Environment.TickCount64 - Volatile.Read(ref _lastAccessUnitAt);
            if (quiet <= timeout.TotalMilliseconds || Interlocked.Exchange(ref reported, 1) == 1) return;
            _post(() => StopFailedStream(publisher,
                $"Your stream was stopped: the encoder stopped sending video for {timeout.TotalSeconds:0.#} seconds."));
        }, null, period, period);
    }

    private void OnAccessUnitEncoded(SharedMemLibrary.Encoding outgoing, ChannelWriter<byte[]> loopback, byte[] accessUnit)
    {
        Volatile.Write(ref _lastAccessUnitAt, Environment.TickCount64);

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

    // UI thread. stops the loopback decoder once the still is on your tile; the publisher keeps sending to the
    // network. a no-op if the stream was already stopped or restarted
    private void FreezeSelfPreview(IncomingVideoPipeline pipeline, ParticipantVideoSink sink)
    {
        if (_selfVideo != pipeline) return;

        var writer = _selfVideoWriter;
        _selfVideo = null;
        _selfVideoWriter = null;

        // the decoder can still put out frames it had buffered before it stops. a disposed sink drops them, so
        // the still stays the first frame and Stop stream can't have a late frame bring the picture back
        sink.Dispose();
        writer?.TryComplete();

        // Stop waits for the pipeline thread and for ffmpeg to exit, which can take seconds: not on the UI thread
        _ = Task.Run(() =>
        {
            try
            {
                pipeline.Stop();
            }
            catch (Exception e)
            {
                Trace.WriteLine("[video] stopping your preview decoder failed: " + e.Message);
            }
        });
    }

    private void StopVideo()
    {
        var publisher = _publisher;
        var writer = _selfVideoWriter;
        var pipeline = _selfVideo;
        _publisher = null;
        _selfVideoWriter = null;
        _selfVideo = null;

        _stallWatch?.Dispose();
        _stallWatch = null;
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

        _remoteDecoderRestarts = 0;
        StartRemoteVideo(_video, source);
    }

    private void StartRemoteVideo(VideoServices video, IEncodedVideoSource source)
    {
        IVideoDecoder decoder;
        try
        {
            decoder = video.CreateDecoder();
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            Error = "Could not show the stream: " + e.Message;
            return;
        }

        var peer = EnsurePeer();
        _remoteVideo?.Stop();

        IncomingVideoPipeline? pipeline = null;
        // a decoder that is working again earns back its restarts
        decoder.FrameDecoded += _ => Volatile.Write(ref _remoteDecoderRestarts, 0);
        decoder.Failed += () => _post(() => OnRemoteDecoderFailed(video, source, pipeline!));
        pipeline = new IncomingVideoPipeline(source, decoder, new ParticipantVideoSink(peer, _post, tracksActivity: true));
        _remoteVideo = pipeline;
        pipeline.Start();
    }

    // UI thread. the source is still good (Stop leaves it open), so a fresh decoder picks up at the next keyframe
    private void OnRemoteDecoderFailed(VideoServices video, IEncodedVideoSource source, IncomingVideoPipeline pipeline)
    {
        if (_remoteVideo != pipeline) return;

        if (_remoteDecoderRestarts >= MaxDecoderRestarts)
        {
            _remoteVideo = null;
            pipeline.Stop();
            var peer = EnsurePeer();
            peer.IsStreamer = false;
            peer.StreamPreview = null;
            Error = "Could not show the stream: the video decoder keeps stopping.";
            return;
        }

        _remoteDecoderRestarts++;
        Trace.WriteLine($"[video] the stream decoder stopped, starting a new one ({_remoteDecoderRestarts} of {MaxDecoderRestarts})");
        StartRemoteVideo(video, source);
    }
}
