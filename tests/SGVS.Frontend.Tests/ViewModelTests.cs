using SGVS.Frontend.Services;
using SGVS.Frontend.Tests.Support;
using SGVS.Frontend.ViewModels;
using Xunit;

namespace SGVS.Frontend.Tests;

public class ViewModelTests
{
    private readonly FakeNetworkingProcess _process = new();
    private readonly FakeSharedMemory _memory = new();

    private NetworkMeetingService NewService() =>
        new("Hazel", () => _process, () => _memory, action => action());

    // ---- Start page ----

    [Fact]
    public async Task Host_opens_the_meeting_page_once_the_offer_is_ready()
    {
        var service = NewService();
        var opened = 0;
        var start = new StartViewModel(service, () => opened++, () => { });

        await start.HostCommand.ExecuteAsync(null);

        Assert.Equal(1, opened);
        Assert.False(start.IsBusy);
    }

    [Fact]
    public async Task A_failed_host_stays_on_start_and_shows_why()
    {
        _process.Script = _ => [new() { Event = "error", Message = "could not host: no network" }];
        var service = NewService();
        var opened = 0;
        var start = new StartViewModel(service, () => opened++, () => { });

        await start.HostCommand.ExecuteAsync(null);

        Assert.Equal(0, opened);
        Assert.True(start.HasError);
        Assert.Equal("could not host: no network", start.ErrorMessage);
    }

    [Fact]
    public async Task Join_shows_the_reply_and_waits_for_the_host()
    {
        var service = NewService();
        var opened = 0;
        var start = new StartViewModel(service, () => opened++, () => { }) { JoinText = "host-code" };

        await start.JoinCommand.ExecuteAsync(null);

        Assert.Equal("join", _process.Sent.Single().Cmd);
        Assert.Equal("host-code", _process.Sent.Single().Code);
        Assert.True(start.IsAwaitingHost);
        Assert.Equal("ANSWER-CODE", start.ReplyCode);
        Assert.Equal(0, opened); // not in the meeting until the host pastes the reply
    }

    [Fact]
    public async Task Join_moves_on_when_the_connection_comes_up()
    {
        var service = NewService();
        var opened = 0;
        var start = new StartViewModel(service, () => opened++, () => { }) { JoinText = "host-code" };
        await start.JoinCommand.ExecuteAsync(null);

        _process.Emit("state", state: "connected");
        _process.Emit("state", state: "connected"); // repeated events must not open it twice

        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task Joining_with_an_empty_box_shows_an_error()
    {
        var start = new StartViewModel(NewService(), () => { }, () => { });

        await start.JoinCommand.ExecuteAsync(null);

        Assert.True(start.HasError);
        Assert.False(start.IsAwaitingHost);
    }

    [Fact]
    public async Task Cancel_while_waiting_starts_over()
    {
        var restarted = 0;
        var service = NewService();
        var start = new StartViewModel(service, () => { }, () => restarted++) { JoinText = "host-code" };
        await start.JoinCommand.ExecuteAsync(null);

        start.CancelCommand.Execute(null);

        Assert.Equal(1, restarted);
    }

    [Fact]
    public async Task A_second_press_while_busy_is_ignored()
    {
        var slow = new FakeNetworkingProcess { Script = _ => [] }; // never answers, so host stays busy
        var service = new NetworkMeetingService("Hazel", () => slow, () => _memory, a => a(), TimeSpan.FromMilliseconds(300));
        var start = new StartViewModel(service, () => { }, () => { });

        var first = start.HostCommand.ExecuteAsync(null);
        await start.HostCommand.ExecuteAsync(null); // ignored, returns straight away
        await first;

        Assert.Single(slow.Sent);
    }

    [Fact]
    public void Previewer_constructor_works_without_a_backend()
    {
        var start = new StartViewModel();

        Assert.False(start.HasError);
        Assert.False(start.IsAwaitingHost);
    }

    // ---- Meeting page ----

    private async Task<MeetingViewModel> HostedMeeting()
    {
        var service = NewService();
        await service.HostAsync();
        return new MeetingViewModel(service, _ => { }, () => { });
    }

    [Fact]
    public async Task Host_meeting_shows_the_code_and_waits_for_a_reply()
    {
        var meeting = await HostedMeeting();

        Assert.True(meeting.IsHost);
        Assert.True(meeting.IsWaitingForReply);
        Assert.Equal("OFFER-CODE", meeting.JoinCode.Code);
        Assert.False(meeting.HasError);
        Assert.False(meeting.IsCallEnded);
    }

    [Fact]
    public async Task Pasting_the_reply_sends_accept_and_clears_the_box()
    {
        var meeting = await HostedMeeting();
        meeting.ReplyText = "  guest-reply ";

        await meeting.AcceptReplyCommand.ExecuteAsync(null);

        var sent = _process.Sent.Last();
        Assert.Equal("accept", sent.Cmd);
        Assert.Equal("guest-reply", sent.Code);
        Assert.Equal("", meeting.ReplyText);
    }

    [Fact]
    public async Task A_bad_reply_shows_an_error_and_keeps_the_box_open()
    {
        var meeting = await HostedMeeting();
        meeting.ReplyText = "garbage";
        await meeting.AcceptReplyCommand.ExecuteAsync(null);

        _process.Emit("error", message: "could not accept reply: bad");

        Assert.True(meeting.HasError);
        Assert.Equal("could not accept reply: bad", meeting.ErrorMessage);
        Assert.True(meeting.IsWaitingForReply);
    }

    [Fact]
    public async Task An_empty_reply_is_refused_and_keeps_the_box()
    {
        var meeting = await HostedMeeting();
        meeting.ReplyText = "";

        await meeting.AcceptReplyCommand.ExecuteAsync(null);

        Assert.True(meeting.HasError);
        Assert.DoesNotContain(_process.Sent, c => c.Cmd == "accept");
    }

    [Fact]
    public async Task The_reply_box_goes_away_once_connected_and_the_peer_appears()
    {
        var meeting = await HostedMeeting();

        _process.Emit("state", state: "connected");
        _process.Emit("peer", peer: "Gus");

        Assert.False(meeting.IsWaitingForReply);
        Assert.Equal(2, meeting.Members.Count);
        Assert.Equal(2, meeting.GalleryColumns);
    }

    [Fact]
    public async Task The_page_says_so_when_the_other_person_leaves()
    {
        var meeting = await HostedMeeting();
        _process.Emit("state", state: "connected");
        _process.Emit("peer", peer: "Gus");

        _process.Emit("peer_left");

        Assert.True(meeting.IsCallEnded);
        Assert.Single(meeting.Members);
    }

    [Fact]
    public async Task Guests_have_no_code_to_share()
    {
        var service = NewService();
        await service.JoinAsync("host-code");
        _process.Emit("state", state: "connected");

        var meeting = new MeetingViewModel(service, _ => { }, () => { });

        Assert.False(meeting.IsHost);
        Assert.False(meeting.IsWaitingForReply);
    }

    [Fact]
    public async Task Starting_and_stopping_a_stream_from_the_overlay_goes_through_the_service()
    {
        var service = NewService();
        await service.HostAsync();
        _process.Emit("state", state: "connected");
        var meeting = new MeetingViewModel(service, _ => { }, () => { });

        await meeting.Overlay.ConfirmDialogCommand.ExecuteAsync(null); // dialog closed: no-op
        meeting.Overlay.OpenStartStreamCommand.Execute(null);
        await meeting.Overlay.ConfirmDialogCommand.ExecuteAsync(null);
        Assert.True(service.Self.IsStreamer);
        Assert.True(meeting.Overlay.IsStreaming);
        Assert.True(_memory.IsSharingVideo);

        await meeting.Overlay.StopStreamCommand.ExecuteAsync(null);
        Assert.False(service.Self.IsStreamer);
        Assert.False(meeting.Overlay.IsStreaming);
    }

    [Fact]
    public async Task Leave_ends_the_call_and_goes_back_to_start()
    {
        var service = NewService();
        await service.HostAsync();
        var wentToStart = 0;
        var meeting = new MeetingViewModel(service, _ => { }, () => wentToStart++);

        await meeting.Overlay.ConfirmLeaveCommand.ExecuteAsync(null);

        Assert.Equal("leave", _process.Sent.Last().Cmd);
        Assert.Equal(1, wentToStart);
    }

    [Fact]
    public void Join_code_rail_previews_only_the_start_of_a_long_connection_string()
    {
        const string code = "xJJda9swGIXvA_kPuh441atvveCLJk7adG3XrSntrWIrqUcsC1vpx37";
        var rail = new JoinCodeViewModel(code);

        Assert.Equal("xJJ", rail.FirstHalf);
        Assert.Equal("da9", rail.SecondHalf);
        Assert.Equal(code, rail.Code); // what gets copied is untouched
    }

    [Theory]
    [InlineData("KX7-29Q", "KX7", "29Q")]
    [InlineData("AB", "AB", "")]
    [InlineData("", "", "")]
    public void Join_code_rail_handles_short_codes(string code, string first, string second)
    {
        var rail = new JoinCodeViewModel(code);

        Assert.Equal(first, rail.FirstHalf);
        Assert.Equal(second, rail.SecondHalf);
    }

    // ---- the shell ----

    [Fact]
    public void The_app_opens_on_the_start_page_with_a_fresh_call()
    {
        var made = 0;
        var main = new MainViewModel(() => { made++; return NewService(); });

        Assert.IsType<StartViewModel>(main.CurrentView);
        Assert.Equal(1, made);
    }

    [Fact]
    public async Task Hosting_from_start_lands_on_the_meeting_and_leaving_returns_to_a_fresh_start()
    {
        var services = new List<NetworkMeetingService>();
        var main = new MainViewModel(() => { var s = NewService(); services.Add(s); return s; });

        await ((StartViewModel)main.CurrentView).HostCommand.ExecuteAsync(null);
        var meeting = Assert.IsType<MeetingViewModel>(main.CurrentView);
        Assert.Equal(1, services.Count);

        await meeting.Overlay.ConfirmLeaveCommand.ExecuteAsync(null);

        Assert.IsType<StartViewModel>(main.CurrentView);
        Assert.Equal(2, services.Count); // the next attempt gets its own call
        Assert.True(_process.Disposed);   // and the old networking process is gone
    }

    [Fact]
    public async Task Joining_from_start_lands_on_the_meeting_when_connected()
    {
        var main = new MainViewModel(NewService);
        var start = (StartViewModel)main.CurrentView;
        start.JoinText = "host-code";

        await start.JoinCommand.ExecuteAsync(null);
        Assert.IsType<StartViewModel>(main.CurrentView);

        _process.Emit("state", state: "connected");

        var meeting = Assert.IsType<MeetingViewModel>(main.CurrentView);
        Assert.False(meeting.IsHost);
    }

    [Fact]
    public async Task Cancelling_a_join_returns_to_a_fresh_start_and_closes_networking()
    {
        var main = new MainViewModel(NewService);
        var start = (StartViewModel)main.CurrentView;
        start.JoinText = "host-code";
        await start.JoinCommand.ExecuteAsync(null);

        start.CancelCommand.Execute(null);

        Assert.NotSame(start, main.CurrentView);
        Assert.IsType<StartViewModel>(main.CurrentView);
        Assert.True(_process.Disposed);
    }

    [Fact]
    public async Task Watching_a_stream_and_stopping_returns_to_the_same_meeting_page()
    {
        var main = new MainViewModel(() => new FakeMeetingService());
        ((StartViewModel)main.CurrentView).HostCommand.Execute(null);
        await Task.Yield();
        var meeting = Assert.IsType<MeetingViewModel>(main.CurrentView);

        meeting.WatchCommand.Execute(meeting.Members.First(m => m.IsStreamer));
        var watch = Assert.IsType<WatchViewModel>(main.CurrentView);
        watch.Overlay.OpenStopWatchingCommand.Execute(null);

        Assert.Same(meeting, main.CurrentView);
    }

    [Fact]
    public async Task Shutdown_closes_the_networking_process()
    {
        var main = new MainViewModel(NewService);
        await ((StartViewModel)main.CurrentView).HostCommand.ExecuteAsync(null);

        main.Shutdown();

        Assert.True(_process.Disposed);
    }

    [Fact]
    public void The_fake_call_still_works_for_offline_use()
    {
        var main = new MainViewModel(() => new FakeMeetingService());

        ((StartViewModel)main.CurrentView).HostCommand.Execute(null);

        var meeting = Assert.IsType<MeetingViewModel>(main.CurrentView);
        Assert.Equal(6, meeting.Members.Count);
    }
}
