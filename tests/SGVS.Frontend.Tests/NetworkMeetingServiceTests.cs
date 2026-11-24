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
}
