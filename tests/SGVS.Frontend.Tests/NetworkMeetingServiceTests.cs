using SGVS.Frontend.Services;
using SGVS.Frontend.Tests.Support;
using Xunit;

namespace SGVS.Frontend.Tests;

public class NetworkMeetingServiceTests
{
    private readonly FakeNetworkingProcess _process = new();
    private readonly FakeSharedMemory _memory = new();
    private int _processesStarted;

    private NetworkMeetingService Create(TimeSpan? timeout = null) =>
        new("Hazel", () => { _processesStarted++; return _process; }, () => _memory, action => action(), timeout);

    private async Task<NetworkMeetingService> Hosting()
    {
        var service = Create();
        Assert.True(await service.HostAsync());
        return service;
    }

    private async Task<NetworkMeetingService> Connected()
    {
        var service = await Hosting();
        _process.Emit("state", state: "connected");
        _process.Emit("peer", peer: "Gus");
        return service;
    }

    // ---- hosting ----

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
    public async Task Host_sends_the_host_command_and_publishes_the_offer()
    {
        var service = Create();

        Assert.True(await service.HostAsync());

        var sent = Assert.Single(_process.Sent);
        Assert.Equal("host", sent.Cmd);
        Assert.Equal("Hazel", sent.Name);
        Assert.Equal("OFFER-CODE", service.Code);
        Assert.Equal(CallState.WaitingForReply, service.State);
        Assert.True(service.IsHost);
        Assert.Null(service.Error);
    }

    [Fact]
    public async Task Host_reports_an_error_from_networking()
    {
        _process.Script = _ => [new() { Event = "error", Message = "could not host: no network" }];
        var service = Create();

        Assert.False(await service.HostAsync());

        Assert.Equal("could not host: no network", service.Error);
        Assert.Equal(CallState.Idle, service.State);
    }

    [Fact]
    public async Task Host_reports_when_the_module_cannot_start()
    {
        var service = new NetworkMeetingService("Hazel", () => throw new FileNotFoundException("module missing"),
            () => _memory, action => action());

        Assert.False(await service.HostAsync());

        Assert.Contains("module missing", service.Error);
        Assert.Equal(CallState.Idle, service.State);
    }

    [Fact]
    public async Task Host_gives_up_when_networking_never_answers()
    {
        _process.Script = _ => [];
        var service = Create(TimeSpan.FromMilliseconds(50));

        Assert.False(await service.HostAsync());

        Assert.Contains("did not respond", service.Error);
    }

    [Fact]
    public async Task Cannot_host_twice()
    {
        var service = await Hosting();

        Assert.False(await service.HostAsync());

        Assert.Equal("Already in a meeting.", service.Error);
        Assert.Equal(1, _processesStarted);
    }

    // ---- joining ----

    [Fact]
    public async Task Join_sends_the_code_and_publishes_the_reply()
    {
        var service = Create();

        Assert.True(await service.JoinAsync("  host-code \n"));

        var sent = Assert.Single(_process.Sent);
        Assert.Equal("join", sent.Cmd);
        Assert.Equal("host-code", sent.Code); // whitespace from pasting is trimmed
        Assert.Equal("Hazel", sent.Name);
        Assert.Equal("ANSWER-CODE", service.Code);
        Assert.Equal(CallState.WaitingForHost, service.State);
        Assert.False(service.IsHost);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Join_needs_a_code(string code)
    {
        var service = Create();

        Assert.False(await service.JoinAsync(code));

        Assert.NotNull(service.Error);
        Assert.Equal(0, _processesStarted);
    }

    [Fact]
    public async Task Join_reports_a_bad_code()
    {
        _process.Script = _ => [new() { Event = "error", Message = "could not join: connection string is not valid" }];
        var service = Create();

        Assert.False(await service.JoinAsync("garbage"));

        Assert.Contains("not valid", service.Error);
        Assert.Equal(CallState.Idle, service.State);
    }

    // ---- accepting the reply ----

    [Fact]
    public async Task Accept_sends_the_reply_to_networking()
    {
        var service = await Hosting();

        Assert.True(await service.AcceptReplyAsync(" reply-code\n"));

        var sent = _process.Sent.Last();
        Assert.Equal("accept", sent.Cmd);
        Assert.Equal("reply-code", sent.Code);
    }

    [Fact]
    public async Task Accept_without_hosting_is_an_error()
    {
        var service = Create();

        Assert.False(await service.AcceptReplyAsync("reply"));

        Assert.NotNull(service.Error);
        Assert.Empty(_process.Sent);
    }

    [Fact]
    public async Task Accept_needs_a_reply()
    {
        var service = await Hosting();

        Assert.False(await service.AcceptReplyAsync("  "));

        Assert.NotNull(service.Error);
        Assert.Single(_process.Sent); // only the host command
    }

    [Fact]
    public async Task A_bad_reply_is_reported_and_the_meeting_stays_open()
    {
        var service = await Hosting();
        await service.AcceptReplyAsync("garbage");

        _process.Emit("error", message: "could not accept reply: bad");

        Assert.Equal("could not accept reply: bad", service.Error);
        Assert.Equal(CallState.WaitingForReply, service.State); // the host can paste another one
    }

    [Fact]
    public async Task A_new_accept_clears_the_old_error()
    {
        var service = await Hosting();
        _process.Emit("error", message: "bad");

        await service.AcceptReplyAsync("next");

        Assert.Null(service.Error);
    }

    // ---- connection and members ----

    [Fact]
    public async Task Connected_state_moves_to_connected()
    {
        var service = await Hosting();

        _process.Emit("state", state: "connected");

        Assert.Equal(CallState.Connected, service.State);
    }

    [Fact]
    public async Task Other_connection_states_do_not_end_the_call_early()
    {
        var service = await Hosting();

        _process.Emit("state", state: "connecting");
        _process.Emit("state", state: "disconnected"); // can recover on its own, peer_left is the real signal

        Assert.Equal(CallState.WaitingForReply, service.State);
        Assert.Null(service.Error);
    }

    [Fact]
    public async Task A_failed_connection_ends_the_call_with_an_error()
    {
        var service = await Connected();

        _process.Emit("state", state: "failed");

        Assert.Equal(CallState.Ended, service.State);
        Assert.NotNull(service.Error);
    }

    [Fact]
    public async Task A_peer_joins_the_gallery()
    {
        var service = await Connected();

        Assert.Equal(["Hazel", "Gus"], service.Members.Select(m => m.Name));
        Assert.False(service.Members[1].IsSelf);
        Assert.False(service.Members[1].IsStreamer);
    }

    [Fact]
    public async Task The_same_peer_is_not_added_twice()
    {
        var service = await Connected();

        _process.Emit("peer", peer: "Gus");

        Assert.Equal(2, service.Members.Count);
    }

    [Fact]
    public async Task A_peer_with_no_name_is_ignored()
    {
        var service = await Hosting();

        _process.Emit("peer", peer: "  ");

        Assert.Single(service.Members);
    }

    [Fact]
    public async Task Peer_stream_events_toggle_their_tile()
    {
        var service = await Connected();
        var peer = service.Members[1];

        _process.Emit("peer_stream", on: true);
        Assert.True(peer.IsStreamer);
        Assert.False(service.Self.IsStreamer);

        _process.Emit("peer_stream", on: false);
        Assert.False(peer.IsStreamer);
    }

    [Fact]
    public async Task Peer_left_removes_them_and_ends_the_call()
    {
        var service = await Connected();

        _process.Emit("peer_left");

        Assert.Single(service.Members);
        Assert.Equal(CallState.Ended, service.State);
    }

    [Fact]
    public async Task Unknown_events_are_ignored()
    {
        var service = await Hosting();

        _process.Emit("something_new", code: "x");
        _process.Emit("offer"); // no code, malformed

        Assert.Equal("OFFER-CODE", service.Code);
        Assert.Equal(CallState.WaitingForReply, service.State);
    }

    // ---- streaming ----

    [Fact]
    public async Task Start_stream_sets_the_flag_tells_the_peer_and_turns_on_sharing()
    {
        var service = await Connected();

        await service.StartStreamAsync(ShareSource.Screen);

        Assert.True(service.Self.IsStreamer);
        Assert.True(_memory.IsSharingVideo);
        var sent = _process.Sent.Last();
        Assert.Equal("stream", sent.Cmd);
        Assert.True(sent.On);
    }

    [Fact]
    public async Task Stop_stream_undoes_all_of_it()
    {
        var service = await Connected();
        await service.StartStreamAsync(ShareSource.App);

        await service.StopStreamAsync();

        Assert.False(service.Self.IsStreamer);
        Assert.False(_memory.IsSharingVideo);
        Assert.False(_process.Sent.Last().On);
    }

    [Fact]
    public async Task Shared_memory_is_only_opened_when_streaming_starts()
    {
        var opened = 0;
        var service = new NetworkMeetingService("Hazel", () => _process, () => { opened++; return _memory; }, a => a());
        await service.HostAsync();
        Assert.Equal(0, opened);

        await service.StartStreamAsync(ShareSource.Screen);
        await service.StopStreamAsync();
        await service.StartStreamAsync(ShareSource.Screen);

        Assert.Equal(1, opened); // opened once and reused
    }

    [Fact]
    public async Task A_stream_that_cannot_open_shared_memory_does_not_go_live()
    {
        _memory.Fail = true;
        var service = await Connected();

        await service.StartStreamAsync(ShareSource.Screen);

        Assert.False(service.Self.IsStreamer);
        Assert.Contains("shared memory", service.Error);
        Assert.DoesNotContain(_process.Sent, c => c.Cmd == "stream");
    }

    [Fact]
    public async Task A_missing_native_library_is_reported_not_thrown()
    {
        var service = new NetworkMeetingService("Hazel", () => _process,
            () => throw new DllNotFoundException("shared_mem_client"), a => a());
        await service.HostAsync();

        await service.StartStreamAsync(ShareSource.Screen);

        Assert.False(service.Self.IsStreamer);
        Assert.NotNull(service.Error);
    }

    [Fact]
    public async Task Streaming_started_before_the_peer_connects_is_announced_on_connect()
    {
        var service = await Hosting();
        await service.StartStreamAsync(ShareSource.Screen);
        _process.Sent.Clear();

        _process.Emit("state", state: "connected");

        var sent = Assert.Single(_process.Sent);
        Assert.Equal("stream", sent.Cmd);
        Assert.True(sent.On);
    }

    // ---- leaving and crashes ----

    [Fact]
    public async Task Leave_tells_networking_closes_everything_and_clears_the_gallery()
    {
        var service = await Connected();
        await service.StartStreamAsync(ShareSource.Screen);

        await service.LeaveAsync();

        Assert.Equal("leave", _process.Sent.Last().Cmd);
        Assert.True(_process.Disposed);
        Assert.True(_memory.Disposed);
        Assert.Equal(CallState.Ended, service.State);
        Assert.Single(service.Members);
        Assert.False(service.Self.IsStreamer);
    }

    [Fact]
    public async Task Leave_before_anything_started_is_harmless()
    {
        var service = Create();

        await service.LeaveAsync();

        Assert.Equal(CallState.Ended, service.State);
    }

    [Fact]
    public async Task Events_after_leaving_are_not_acted_on()
    {
        var service = await Connected();
        await service.LeaveAsync();

        _process.Emit("peer", peer: "Latecomer");

        Assert.Single(service.Members);
    }

    [Fact]
    public async Task Can_start_a_new_call_after_leaving()
    {
        var service = await Connected();
        await service.LeaveAsync();

        Assert.True(await service.HostAsync());

        Assert.Equal(CallState.WaitingForReply, service.State);
        Assert.Equal(2, _processesStarted);
    }

    [Fact]
    public async Task A_crashed_module_ends_the_call_with_an_error()
    {
        var service = await Connected();

        _process.Crash();

        Assert.Equal(CallState.Ended, service.State);
        Assert.Contains("stopped unexpectedly", service.Error);
    }

    [Fact]
    public async Task A_crash_during_setup_fails_the_host_call_instead_of_hanging()
    {
        _process.Script = _ => { _process.Crash(); return []; };
        var service = Create();

        Assert.False(await service.HostAsync());

        Assert.Contains("stopped unexpectedly", service.Error);
    }

    [Fact]
    public async Task Property_changes_are_raised_for_the_ui()
    {
        var changed = new List<string?>();
        var service = Create();
        service.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await service.HostAsync();

        Assert.Contains(nameof(IMeetingService.Code), changed);
        Assert.Contains(nameof(IMeetingService.State), changed);
        Assert.Contains(nameof(IMeetingService.IsHost), changed);
    }
}
