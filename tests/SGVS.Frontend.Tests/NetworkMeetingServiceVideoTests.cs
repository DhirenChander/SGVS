using System.Threading.Channels;
using Avalonia.Headless.XUnit;
using SGVS.Frontend.Services;
using SGVS.Frontend.Services.Video;
using SGVS.Frontend.Tests.Support;
using Xunit;

namespace SGVS.Frontend.Tests;

public class NetworkMeetingServiceVideoTests
{
    private readonly FakeFrameBuffer _fb = new();
    private readonly List<FakeStreamPublisher> _publishers = [];
    private readonly List<FakeVideoDecoder> _decoders = [];

    private IStreamPublisher NewPublisher()
    {
        var publisher = new FakeStreamPublisher();
        _publishers.Add(publisher);
        return publisher;
    }

    private IVideoDecoder NewDecoder()
    {
        var decoder = new FakeVideoDecoder();
        _decoders.Add(decoder);
        return decoder;
    }

    private NetworkMeetingService Create(Func<IStreamPublisher>? publisherFactory = null, Func<IVideoDecoder>? decoderFactory = null,
        Action<Action>? post = null, TimeSpan? stallTimeout = null) =>
        new("Hazel", () => new FakeNetworkingProcess(), post ?? (action => action()), _fb,
            video: new VideoServices(publisherFactory ?? NewPublisher, decoderFactory ?? NewDecoder, stallTimeout));

    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline) Thread.Sleep(10);
    }

    [Fact]
    public void Start_stream_starts_the_publisher_and_marks_you_streaming()
    {
        var service = Create();

        service.StartStream(ShareSource.Screen);

        Assert.True(service.Self.IsStreamer);
        Assert.Null(service.Error);
        Assert.True(Assert.Single(_publishers).Started);
    }

    [Fact]
    public void Encoded_access_units_go_to_shared_memory_and_your_own_decoder()
    {
        var service = Create();
        service.StartStream(ShareSource.Screen);

        _publishers[0].Emit([0, 0, 0, 1, 9, 0xF0, 7]);

        var (enc, data) = Assert.Single(_fb.Pushed);
        Assert.Equal(SharedMemLibrary.EncodingType.Video, enc.type);
        Assert.Equal(SharedMemLibrary.EncodingDir.Outgoing, enc.dir);
        Assert.Equal([0, 0, 0, 1, 9, 0xF0, 7], data);
        WaitUntil(() => _decoders.Count == 1 && _decoders[0].Received.Length == 1);
        Assert.Equal([0, 0, 0, 1, 9, 0xF0, 7], Assert.Single(_decoders[0].Received));
    }

    // your own tile keeps the first decoded frame as a still: decoding stops, sending to the network does not
    [AvaloniaFact]
    public void First_decoded_frame_freezes_your_tile_and_stops_your_decoder()
    {
        var service = Create();
        service.StartStream(ShareSource.Screen);

        _decoders[0].Raise(new byte[VideoFormat.FrameBytes]);
        WaitUntil(() => _decoders[0].Disposed);

        Assert.True(_decoders[0].Disposed);
        Assert.NotNull(service.Self.StreamPreview);
        Assert.True(service.Self.IsStreamer);
        Assert.False(_publishers[0].Disposed);

        _publishers[0].Emit([0, 0, 0, 1, 9, 0xF0, 7]);
        Assert.Single(_fb.Pushed);
        Assert.Empty(_decoders[0].Received);
    }

    // the decoder only stops in the background, and can still put out frames it had buffered. none of them
    // may replace the still, and once you stop streaming none may bring a picture back
    [AvaloniaFact]
    public void Frames_after_the_freeze_do_not_change_your_still()
    {
        var service = Create();
        service.StartStream(ShareSource.Screen);
        var decoder = _decoders[0];

        decoder.Raise(new byte[VideoFormat.FrameBytes]);
        var still = service.Self.StreamPreview;
        decoder.Raise(new byte[VideoFormat.FrameBytes]);
        Assert.Same(still, service.Self.StreamPreview);

        service.StopStream();
        decoder.Raise(new byte[VideoFormat.FrameBytes]);
        Assert.Null(service.Self.StreamPreview);
    }

    // the freeze is posted to the UI thread. if the stream was restarted before it ran, the new stream's
    // preview must be left alone
    [AvaloniaFact]
    public void A_late_freeze_from_an_old_stream_leaves_the_new_one_running()
    {
        var queued = new Queue<Action>();
        var service = Create(post: queued.Enqueue);
        service.StartStream(ShareSource.Screen);

        _decoders[0].Raise(new byte[VideoFormat.FrameBytes]);
        queued.Dequeue()(); // the frame lands on the tile and queues the freeze

        service.StopStream();
        service.StartStream(ShareSource.Screen);
        while (queued.Count > 0) queued.Dequeue()(); // the old stream's freeze runs now

        _publishers[1].Emit([0, 0, 0, 1, 9, 0xF0, 7]);
        WaitUntil(() => _decoders[1].Received.Length == 1);
        Assert.False(_decoders[1].Disposed);
        Assert.Single(_decoders[1].Received);
    }

    [Fact]
    public void Start_stream_reports_missing_ffmpeg()
    {
        var service = Create(() => throw new InvalidOperationException(Ffmpeg.NotFoundMessage));

        service.StartStream(ShareSource.Screen);

        Assert.StartsWith("Could not start the stream: FFmpeg was not found", service.Error);
        Assert.False(service.Self.IsStreamer);
    }

    [Fact]
    public void Stop_then_start_reuses_the_outgoing_encoding_and_disposes_the_old_publisher()
    {
        var service = Create();

        service.StartStream(ShareSource.Screen);
        service.StopStream();
        service.StartStream(ShareSource.Screen);

        Assert.Single(_fb.Requested);
        Assert.True(_publishers[0].Disposed);
        Assert.True(_decoders[0].Disposed);
        Assert.True(_publishers[1].Started);
        Assert.False(_publishers[1].Disposed);
        Assert.True(service.Self.IsStreamer);
    }

    [Fact]
    public void Publisher_failure_stops_the_stream_with_an_error()
    {
        var service = Create();
        service.StartStream(ShareSource.Screen);

        _publishers[0].Fail("boom");

        Assert.False(service.Self.IsStreamer);
        Assert.Equal("boom", service.Error);
        Assert.True(_publishers[0].Disposed);
    }

    [Fact]
    public void A_late_failure_from_an_old_publisher_leaves_the_new_stream_running()
    {
        var service = Create();
        service.StartStream(ShareSource.Screen);
        service.StopStream();
        service.StartStream(ShareSource.Screen);

        _publishers[0].Fail("old encoder exited");

        Assert.True(service.Self.IsStreamer);
        Assert.Null(service.Error);
        Assert.False(_publishers[1].Disposed);
    }

    [Fact]
    public void An_encoder_that_stops_sending_video_stops_the_stream_with_an_error()
    {
        var service = Create(stallTimeout: TimeSpan.FromMilliseconds(200));
        service.StartStream(ShareSource.Screen);
        _publishers[0].Emit([0, 0, 0, 1, 9, 0xF0, 7]);

        WaitUntil(() => !service.Self.IsStreamer);

        Assert.False(service.Self.IsStreamer);
        Assert.Contains("stopped sending video", service.Error);
        Assert.True(_publishers[0].Disposed);
    }

    [Fact]
    public void An_encoder_that_keeps_sending_video_is_left_running()
    {
        var service = Create(stallTimeout: TimeSpan.FromMilliseconds(300));
        service.StartStream(ShareSource.Screen);

        for (var i = 0; i < 20; i++)
        {
            _publishers[0].Emit([0, 0, 0, 1, 9, 0xF0, 7]);
            Thread.Sleep(50);
        }

        Assert.True(service.Self.IsStreamer);
        Assert.Null(service.Error);
    }

    [Fact]
    public void A_remote_decoder_that_dies_is_replaced()
    {
        var service = Create();
        var channel = Channel.CreateUnbounded<byte[]>();
        service.AttachRemoteVideo(new ChannelVideoSource(channel.Reader));

        _decoders[0].Die();

        Assert.Equal(2, _decoders.Count);
        Assert.True(_decoders[0].Disposed);
        Assert.Null(service.Error);
        channel.Writer.TryWrite([1, 2, 3]);
        WaitUntil(() => _decoders[1].Received.Length == 1);
        Assert.Equal([1, 2, 3], Assert.Single(_decoders[1].Received));
    }

    [Fact]
    public void A_remote_decoder_that_keeps_dying_gives_up_with_an_error()
    {
        var service = Create();
        service.AttachRemoteVideo(new ChannelVideoSource(Channel.CreateUnbounded<byte[]>().Reader));
        var peer = service.Members.Single(m => !m.IsSelf);
        peer.IsStreamer = true;

        for (var i = 0; i < 4; i++) _decoders[i].Die();

        Assert.Equal(4, _decoders.Count); // the first one plus three replacements
        Assert.All(_decoders, d => Assert.True(d.Disposed));
        Assert.Contains("video decoder keeps stopping", service.Error);
        Assert.False(peer.IsStreamer);
    }

    // a decoder that dies now and then over a long call is fine, as long as each new one gets going
    [Fact]
    public void A_replacement_decoder_that_decodes_a_frame_earns_back_the_restarts()
    {
        var service = Create();
        service.AttachRemoteVideo(new ChannelVideoSource(Channel.CreateUnbounded<byte[]>().Reader));

        for (var i = 0; i < 3; i++) _decoders[i].Die();
        _decoders[3].Raise([0]); // a frame came out (wrong size, so the tile ignores it)
        _decoders[3].Die();

        Assert.Equal(5, _decoders.Count);
        Assert.Null(service.Error);
    }

    [Fact]
    public void Remote_video_adds_one_peer_and_feeds_the_decoder()
    {
        var service = Create();
        var channel = Channel.CreateUnbounded<byte[]>();

        service.AttachRemoteVideo(new ChannelVideoSource(channel.Reader));
        service.OnParticipantJoined("x");
        channel.Writer.TryWrite([1, 2, 3]);

        Assert.Equal(2, service.Members.Count);
        WaitUntil(() => _decoders.Count == 1 && _decoders[0].Received.Length == 1);
        Assert.Equal([1, 2, 3], Assert.Single(_decoders[0].Received));
    }

    [Fact]
    public void Start_stream_reports_an_ffmpeg_that_cannot_run()
    {
        var service = Create(() =>
        {
            var publisher = new FakeStreamPublisher { StartError = new System.ComponentModel.Win32Exception("not a valid application") };
            _publishers.Add(publisher);
            return publisher;
        });

        service.StartStream(ShareSource.Screen);

        Assert.StartsWith("Could not start the stream: not a valid application", service.Error);
        Assert.False(service.Self.IsStreamer);
        Assert.True(_publishers[0].Disposed);
        Assert.True(_decoders[0].Disposed); // the loopback pipeline it started was torn down again
    }

    [Fact]
    public void Start_stream_reports_a_decoder_that_cannot_start()
    {
        var service = Create(decoderFactory: () => throw new System.ComponentModel.Win32Exception("not a valid application"));

        service.StartStream(ShareSource.Screen);

        Assert.StartsWith("Could not start the stream: not a valid application", service.Error);
        Assert.False(service.Self.IsStreamer);
        Assert.True(Assert.Single(_publishers).Disposed);
    }

    [Fact]
    public void Remote_video_with_a_decoder_that_cannot_start_reports_an_error()
    {
        var service = Create(decoderFactory: () => throw new System.ComponentModel.Win32Exception("not a valid application"));

        service.AttachRemoteVideo(new ChannelVideoSource(Channel.CreateUnbounded<byte[]>().Reader));

        Assert.StartsWith("Could not show the stream: not a valid application", service.Error);
    }

    [Fact]
    public void Disposing_the_call_stops_your_stream_and_the_remote_video()
    {
        var service = Create();
        service.StartStream(ShareSource.Screen);
        service.AttachRemoteVideo(new ChannelVideoSource(Channel.CreateUnbounded<byte[]>().Reader));

        service.Dispose();

        Assert.True(_publishers[0].Disposed);
        Assert.Equal(2, _decoders.Count);
        Assert.All(_decoders, d => Assert.True(d.Disposed));
        Assert.False(service.Self.IsStreamer);
    }

    [Fact]
    public void Without_video_services_start_stream_behaves_as_before()
    {
        var service = new NetworkMeetingService("Hazel", () => new FakeNetworkingProcess(), action => action(), _fb);

        service.StartStream(ShareSource.Screen);

        Assert.True(service.Self.IsStreamer);
        Assert.Empty(_publishers);
        Assert.Empty(_fb.Pushed);
    }
}
