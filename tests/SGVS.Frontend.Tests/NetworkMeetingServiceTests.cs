using SGVS.Frontend.Services;
using SGVS.Frontend.Tests.Support;
using SharedMemLibrary;
using Xunit;

namespace SGVS.Frontend.Tests;

public class NetworkMeetingServiceTests
{
    private readonly FakeNetworkingProcess _process = new();

    private NetworkMeetingService Create(FakeFrameBuffer? fb = null) =>
        new("Hazel", () => _process, action => action(), fb ?? new FakeFrameBuffer());

    [Fact]
    public void Starts_idle_with_only_yourself_in_the_call()
    {
        var service = Create();

        Assert.Equal(CallState.Idle, service.State);
        Assert.Equal("", service.Code);
        Assert.Null(service.Error);
        var self = Assert.Single(service.Members);
        Assert.Same(service.Self, self);
        Assert.True(self.IsSelf);
        Assert.Equal("Hazel", self.Name);
    }

    [Fact]
    public void Host_sends_StartHost_to_networking()
    {
        var fb = new FakeFrameBuffer();
        var service = Create(fb);

        Assert.True(service.Host());

        Assert.True(service.IsHost);
        Assert.Equal(Operation.StartHost, Assert.Single(fb.Messages).Opcode);
    }

    [Fact]
    public void Join_sends_JoinCall_and_makes_you_a_guest()
    {
        var fb = new FakeFrameBuffer();
        var service = Create(fb);

        Assert.True(service.Join("  host-code \n"));

        Assert.False(service.IsHost);
        Assert.Equal(Operation.JoinCall, Assert.Single(fb.Messages).Opcode);
    }

    [Fact]
    public void Accepting_a_reply_longer_than_255_bytes_reports_an_error_instead_of_crashing()
    {
        var service = Create();

        Assert.False(service.AcceptReply(new string('A', 700)));

        Assert.StartsWith("Could not send the reply", service.Error);
    }

    [Fact]
    public void Start_stream_marks_you_as_streaming_and_stop_stream_undoes_it()
    {
        var service = Create();

        service.StartStream(ShareSource.Screen);
        Assert.True(service.Self.IsStreamer);
        Assert.Null(service.Error);

        service.StopStream();
        Assert.False(service.Self.IsStreamer);
        Assert.Null(service.Self.StreamPreview);
    }

    [Fact]
    public void Muting_during_a_call_tells_networking_to_stop_sending_your_mic()
    {
        var fb = new FakeFrameBuffer();
        var service = Create(fb);
        service.Host();

        service.Self.IsMuted = true;
        service.Self.IsMuted = false;

        Assert.Equal([Operation.StartHost, Operation.MicMuted, Operation.MicUnmuted], fb.Messages.Select(m => m.Opcode));
    }

    // the button must never say muted while networking is still sending your mic
    [Fact]
    public void A_mute_that_could_not_be_sent_is_undone_with_an_error()
    {
        var fb = new FakeFrameBuffer();
        var service = Create(fb);
        service.Host();
        fb.FullMessages = true;

        service.Self.IsMuted = true;

        Assert.False(service.Self.IsMuted);
        Assert.StartsWith("Could not mute your mic", service.Error);
        Assert.Equal([Operation.StartHost], fb.Messages.Select(m => m.Opcode));
    }

    [Fact]
    public void Muting_before_the_call_is_sent_once_the_call_starts()
    {
        var fb = new FakeFrameBuffer();
        var service = Create(fb);

        service.Self.IsMuted = true;
        Assert.Empty(fb.Messages);

        service.Join("host-code");

        Assert.Equal([Operation.JoinCall, Operation.MicMuted], fb.Messages.Select(m => m.Opcode));
    }
}
