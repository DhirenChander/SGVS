using SGVS.Frontend.Services;
using SGVS.Frontend.Tests.Support;
using Xunit;

namespace SGVS.Frontend.Tests;

public class NetworkMeetingServiceTests
{
    private readonly FakeNetworkingProcess _process = new();
    private int _processesStarted;

    // the service opens the shared frame buffer itself now, so the factory it is given never gets used
    private NetworkMeetingService Create() =>
        new("Hazel", () => { _processesStarted++; return _process; },
            () => throw new NotSupportedException(), action => action());

    private static NetworkMeetingService CreateWithMissingModule() =>
        new("Hazel", () => throw new FileNotFoundException("module missing"),
            () => throw new NotSupportedException(), action => action());

    // host, join and streaming open the native shared memory, which only exists on Windows
    private static void NeedsSharedMemory() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "needs the native shared memory library");

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
        Assert.Equal(0, _processesStarted); // nothing is launched until you host or join
    }

    [Fact]
    public void Host_reports_when_the_module_cannot_start()
    {
        var service = CreateWithMissingModule();

        Assert.False(service.Host());

        Assert.StartsWith("Could not start the meeting", service.Error);
        Assert.Contains("module missing", service.Error);
        Assert.Equal(CallState.Idle, service.State);
    }

    [Fact]
    public void Join_reports_when_the_module_cannot_start()
    {
        var service = CreateWithMissingModule();

        Assert.False(service.Join("host-code"));

        Assert.StartsWith("Could not join the meeting", service.Error);
        Assert.Contains("module missing", service.Error);
    }

    [Fact]
    public void Host_starts_the_module_once_and_makes_you_the_host()
    {
        NeedsSharedMemory();
        var service = Create();

        Assert.True(service.Host());

        Assert.True(service.IsHost);
        Assert.Equal(1, _processesStarted);
    }

    [Fact]
    public void Join_makes_you_a_guest()
    {
        NeedsSharedMemory();
        var service = Create();

        Assert.True(service.Join("  host-code \n"));

        Assert.False(service.IsHost);
        Assert.Equal(1, _processesStarted);
    }

    [Fact]
    public void Start_stream_marks_you_as_streaming_and_stop_stream_undoes_it()
    {
        NeedsSharedMemory();
        var service = Create();

        service.StartStream(ShareSource.Screen);
        Assert.True(service.Self.IsStreamer);
        Assert.Null(service.Error);

        service.StopStream();
        Assert.False(service.Self.IsStreamer);
        Assert.Null(service.Self.StreamPreview);
    }

    [Fact]
    public void A_crashed_module_ends_the_call_with_an_error()
    {
        NeedsSharedMemory();
        var service = Create();
        service.Host();

        _process.Crash();

        Assert.Equal(CallState.Ended, service.State);
        Assert.Equal("The networking module stopped unexpectedly.", service.Error);
    }
}
