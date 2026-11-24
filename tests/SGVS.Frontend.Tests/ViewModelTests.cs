using SGVS.Frontend.Models;
using SGVS.Frontend.Services;
using SGVS.Frontend.Tests.Support;
using SGVS.Frontend.ViewModels;
using Xunit;

namespace SGVS.Frontend.Tests;

public class ViewModelTests
{
    private readonly ScriptedMeetingService _service = new();

    // ---- Start page ----

    [Fact]
    public void Host_opens_the_meeting_page()
    {
        var opened = 0;
        var start = new StartViewModel(_service, () => opened++, () => { });

        start.HostCommand.Execute(null);

        Assert.Equal(1, opened);
        Assert.False(start.IsBusy);
        Assert.Equal(["host"], _service.Calls);
    }

    [Fact]
    public void A_failed_host_stays_on_start_and_shows_why()
    {
        _service.Succeeds = false;
        _service.FailWith = "could not host: no network";
        var opened = 0;
        var start = new StartViewModel(_service, () => opened++, () => { });

        start.HostCommand.Execute(null);

        Assert.Equal(0, opened);
        Assert.True(start.HasError);
        Assert.Equal("could not host: no network", start.ErrorMessage);
    }

    [Fact]
    public void Join_opens_the_meeting_straight_away()
    {
        var opened = 0;
        var start = new StartViewModel(_service, () => opened++, () => { }) { JoinText = "host-code" };

        start.JoinCommand.Execute(null);

        // the connection strings are exchanged in the networking console for now, so Join sends no code
        Assert.Equal(["join:"], _service.Calls);
        Assert.Equal(1, opened);
    }

    [Fact]
    public void Join_moves_on_when_the_connection_comes_up()
    {
        var opened = 0;
        var start = new StartViewModel(_service, () => opened++, () => { }) { JoinText = "host-code" };
        start.JoinCommand.Execute(null);

        _service.State = CallState.Connected;
        _service.State = CallState.Ended;
        _service.State = CallState.Connected; // must not open it twice

        Assert.Equal(1, opened);
    }

    [Fact]
    public void Cancel_while_waiting_starts_over()
    {
        var restarted = 0;
        var start = new StartViewModel(_service, () => { }, () => restarted++) { JoinText = "host-code" };
        start.JoinCommand.Execute(null);

        start.CancelCommand.Execute(null);

        Assert.Equal(1, restarted);
    }

    [Fact]
    public void Previewer_constructor_works_without_a_backend()
    {
        var start = new StartViewModel();

        Assert.False(start.HasError);
        Assert.False(start.IsAwaitingHost);
    }

    // ---- Meeting page ----

    private MeetingViewModel HostedMeeting()
    {
        _service.Host();
        return new MeetingViewModel(_service, _ => { }, () => { });
    }

    [Fact]
    public void Host_meeting_shows_the_code_and_waits_for_a_reply()
    {
        var meeting = HostedMeeting();

        Assert.True(meeting.IsHost);
        Assert.True(meeting.IsWaitingForReply);
        Assert.Equal("OFFER-CODE", meeting.JoinCode.Code);
        Assert.False(meeting.HasError);
        Assert.False(meeting.IsCallEnded);
    }

    [Fact]
    public void Pasting_the_reply_passes_it_on_and_clears_the_box()
    {
        var meeting = HostedMeeting();
        meeting.ReplyText = "guest-reply";

        meeting.AcceptReplyCommand.Execute(null);

        Assert.Equal("accept:guest-reply", _service.Calls.Last());
        Assert.Equal("", meeting.ReplyText);
    }

    [Fact]
    public void A_bad_reply_shows_an_error_and_keeps_the_box_open()
    {
        var meeting = HostedMeeting();
        _service.Succeeds = false;
        _service.FailWith = "could not accept reply: bad";
        meeting.ReplyText = "garbage";

        meeting.AcceptReplyCommand.Execute(null);

        Assert.True(meeting.HasError);
        Assert.Equal("could not accept reply: bad", meeting.ErrorMessage);
        Assert.Equal("garbage", meeting.ReplyText);
        Assert.True(meeting.IsWaitingForReply);
    }

    [Fact]
    public void The_reply_box_goes_away_once_connected_and_the_peer_appears()
    {
        var meeting = HostedMeeting();

        _service.State = CallState.Connected;
        _service.AddPeer("Gus");

        Assert.False(meeting.IsWaitingForReply);
        Assert.Equal(2, meeting.Members.Count);
        Assert.Equal(2, meeting.GalleryColumns);
    }

    [Fact]
    public void The_page_says_so_when_the_call_ends()
    {
        var meeting = HostedMeeting();
        _service.State = CallState.Connected;

        _service.State = CallState.Ended;

        Assert.True(meeting.IsCallEnded);
    }

    [Fact]
    public void Guests_have_no_code_to_share()
    {
        _service.Join("host-code");
        _service.State = CallState.Connected;

        var meeting = new MeetingViewModel(_service, _ => { }, () => { });

        Assert.False(meeting.IsHost);
        Assert.False(meeting.IsWaitingForReply);
    }

    [Fact]
    public void Starting_and_stopping_a_stream_from_the_overlay_goes_through_the_service()
    {
        _service.Host();
        _service.State = CallState.Connected;
        var meeting = new MeetingViewModel(_service, _ => { }, () => { });

        meeting.Overlay.ConfirmDialogCommand.Execute(null); // dialog closed: no-op
        Assert.Empty(_service.Calls.Where(c => c.StartsWith("start")));

        meeting.Overlay.OpenStartStreamCommand.Execute(null);
        meeting.Overlay.ConfirmDialogCommand.Execute(null);
        Assert.Contains("start:Screen", _service.Calls);
        Assert.True(_service.Self.IsStreamer);
        Assert.True(meeting.Overlay.IsStreaming);

        meeting.Overlay.StopStreamCommand.Execute(null);
        Assert.Contains("stop", _service.Calls);
        Assert.False(_service.Self.IsStreamer);
        Assert.False(meeting.Overlay.IsStreaming);
    }

    [Fact]
    public async Task Leave_goes_back_to_start()
    {
        _service.Host();
        var wentToStart = 0;
        var meeting = new MeetingViewModel(_service, _ => { }, () => wentToStart++);

        await meeting.Overlay.ConfirmLeaveCommand.ExecuteAsync(null);

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
        var main = new MainViewModel(() => { made++; return new ScriptedMeetingService(); });

        Assert.IsType<StartViewModel>(main.CurrentView);
        Assert.Equal(1, made);
    }

    [Fact]
    public async Task Hosting_from_start_lands_on_the_meeting_and_leaving_returns_to_a_fresh_start()
    {
        var services = new List<ScriptedMeetingService>();
        var main = new MainViewModel(() => { var s = new ScriptedMeetingService(); services.Add(s); return s; });

        ((StartViewModel)main.CurrentView).HostCommand.Execute(null);
        var meeting = Assert.IsType<MeetingViewModel>(main.CurrentView);
        Assert.Single(services);

        await meeting.Overlay.ConfirmLeaveCommand.ExecuteAsync(null);

        Assert.IsType<StartViewModel>(main.CurrentView);
        Assert.Equal(2, services.Count); // the next attempt gets its own call
    }

    [Fact]
    public void Joining_from_start_lands_on_the_meeting()
    {
        var main = new MainViewModel(() => _service);
        var start = (StartViewModel)main.CurrentView;
        start.JoinText = "host-code";

        start.JoinCommand.Execute(null);

        var meeting = Assert.IsType<MeetingViewModel>(main.CurrentView);
        Assert.False(meeting.IsHost);
    }

    [Fact]
    public void Cancelling_a_join_returns_to_a_fresh_start()
    {
        var main = new MainViewModel(() => new ScriptedMeetingService());
        var start = (StartViewModel)main.CurrentView;
        start.JoinText = "host-code";
        start.JoinCommand.Execute(null);

        start.CancelCommand.Execute(null);

        Assert.NotSame(start, main.CurrentView);
        Assert.IsType<StartViewModel>(main.CurrentView);
    }

    [Fact]
    public void Going_back_to_start_ends_the_old_call()
    {
        var service = new ScriptedMeetingService();
        var main = new MainViewModel(() => service);

        main.GoToStartPageCommand.Execute(null);

        Assert.True(service.Disposed); // otherwise its ffmpeg processes keep streaming
    }

    [Fact]
    public void Closing_the_app_ends_the_call()
    {
        var service = new ScriptedMeetingService();
        var main = new MainViewModel(() => service);

        main.Shutdown();

        Assert.True(service.Disposed);
    }

    [Fact]
    public void Watching_a_stream_and_stopping_returns_to_the_same_meeting_page()
    {
        var main = new MainViewModel(() => new FakeMeetingService());
        ((StartViewModel)main.CurrentView).HostCommand.Execute(null);
        var meeting = Assert.IsType<MeetingViewModel>(main.CurrentView);

        meeting.WatchCommand.Execute(meeting.Members.First(m => m.IsStreamer));
        var watch = Assert.IsType<WatchViewModel>(main.CurrentView);
        watch.Overlay.OpenStopWatchingCommand.Execute(null);

        Assert.Same(meeting, main.CurrentView);
    }

    [Fact]
    public void The_fake_call_still_works_for_offline_use()
    {
        var main = new MainViewModel(() => new FakeMeetingService());

        ((StartViewModel)main.CurrentView).HostCommand.Execute(null);

        var meeting = Assert.IsType<MeetingViewModel>(main.CurrentView);
        Assert.Equal(6, meeting.Members.Count);
    }

    [Fact]
    public void Mic_mute_is_the_same_on_the_meeting_and_watch_bars()
    {
        var self = new Participant { Name = "Hazel", IsSelf = true };
        var meetingBar = new OverlayViewModel(self, false, null, null, () => Task.CompletedTask, null);
        meetingBar.IsMuted = true;

        var watchBar = new OverlayViewModel(self, true, null, null, () => Task.CompletedTask, null);
        Assert.True(watchBar.IsMuted);

        watchBar.IsMuted = false;
        Assert.False(self.IsMuted);
        Assert.False(meetingBar.IsMuted);
        Assert.Equal("Mute", meetingBar.MuteLabel);
    }
}
