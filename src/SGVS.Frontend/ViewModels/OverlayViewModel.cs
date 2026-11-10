using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SGVS.Frontend.Models;
using SGVS.Frontend.Services;

namespace SGVS.Frontend.ViewModels;

// which options dialog (if any) is currently sitting over the screen. None = closed
public enum StreamDialogKind
{
    None,
    StartStream,
    Leave,
}

// state behind the floating control bar and settings gear (StreamOverlay). Meeting and Watch
// each make their own and pass in what differs:
//   - showViewerControls turns on resolution, volume, stop watching, fullscreen and
//     "show members". those only make sense while watching, so Meeting leaves it off
//   - what Start stream, Stop stream, Leave and Stop watching do comes in as callbacks
public partial class OverlayViewModel : ViewModelBase
{
    // what the Start stream dialog lists. keep the order in step with ShareSources, the
    // selected label's index picks the matching source
    private static readonly string[] SourceLabels = ["Entire screen", "Application window"];
    private static readonly ShareSource[] ShareSources = [ShareSource.Screen, ShareSource.App];

    private readonly Participant? _self;
    private readonly Func<ShareSource, Task>? _startStream;
    private readonly Func<Task>? _stopStream;
    private readonly Func<Task> _leave;
    private readonly Action? _stopWatching;

    private double _volumeBeforeMute = 50;

    // for the XAML previewer only
    public OverlayViewModel() : this(null, true, null, null, () => Task.CompletedTask, null)
    {
    }

    // self: you, so the mic button can flip your Participant to muted (null skips that).
    // startStream: null makes the dialog just close, Watch has no stream of its own to start yet.
    // stopWatching: what the Stop watching button does, Watch uses it to go back to Meeting.
    public OverlayViewModel(
        Participant? self,
        bool showViewerControls,
        Func<ShareSource, Task>? startStream,
        Func<Task>? stopStream,
        Func<Task> leave,
        Action? stopWatching)
    {
        _stopWatching = stopWatching;
        _self = self;
        _startStream = startStream;
        _stopStream = stopStream;
        _leave = leave;
        ShowViewerControls = showViewerControls;
        SelectedStreamSource = SourceLabels[0];
    }

    // resolution / volume / stop watching / fullscreen / "show members" only mean something
    // while watching a stream
    public bool ShowViewerControls { get; }

    // ---- shared by both pages ----

    // your mic. also written to your Participant so the tile can show it
    public bool IsMuted
    {
        get;
        set
        {
            if (!SetProperty(ref field, value)) return;
            if (_self is not null) _self.IsMuted = value;
            OnPropertyChanged(nameof(MuteLabel));
        }
    }

    public string MuteLabel => IsMuted ? "Unmute" : "Mute";

    // true while you are sharing. the bar swaps Start stream for Stop stream
    [ObservableProperty]
    public partial bool IsStreaming { get; set; }

    // "don't ask me again" on the leave dialog. session-only, nothing to persist it to yet
    [ObservableProperty]
    public partial bool SkipLeaveConfirmation { get; set; }

    // settings menu prefs. frontend-only, they reset every launch
    [ObservableProperty]
    public partial bool ShowJoinCode { get; set; } = true;

    // skips the idle auto-hide, bar and gear just stay up
    [ObservableProperty]
    public partial bool KeepControlsVisible { get; set; }

    // ---- dialogs ----

    public StreamDialogKind DialogKind
    {
        get;
        set
        {
            if (!SetProperty(ref field, value)) return;
            OnPropertyChanged(nameof(IsDialogOpen));
            OnPropertyChanged(nameof(IsStartStreamDialog));
            OnPropertyChanged(nameof(IsLeaveDialog));
            OnPropertyChanged(nameof(DialogConfirmLabel));
        }
    }

    public bool IsDialogOpen => DialogKind != StreamDialogKind.None;

    public bool IsStartStreamDialog => DialogKind == StreamDialogKind.StartStream;

    public bool IsLeaveDialog => DialogKind == StreamDialogKind.Leave;

    // the shared Cancel / confirm row only appears for Start stream now, Leave has its own buttons
    public string DialogConfirmLabel => "Start stream";

    public IReadOnlyList<string> StreamSources => SourceLabels;

    // the radio the user picked in the Start stream dialog

    [ObservableProperty]
    public partial string SelectedStreamSource { get; set; }

    [RelayCommand]
    private void OpenStartStream() => DialogKind = StreamDialogKind.StartStream;

    // no dialog for this one, it just hands off to whoever built us
    [RelayCommand]
    private void OpenStopWatching() => _stopWatching?.Invoke();

    [RelayCommand]
    private void CloseDialog() => DialogKind = StreamDialogKind.None;

    // confirm button of the Start stream dialog. closes first so the dialog doesn't hang
    // around while the stream spins up
    [RelayCommand]
    private async Task ConfirmDialog()
    {
        var wasStartStream = IsStartStreamDialog;
        DialogKind = StreamDialogKind.None;

        if (wasStartStream && _startStream is not null)
        {
            var index = Math.Max(0, Array.IndexOf(SourceLabels, SelectedStreamSource));
            await _startStream(ShareSources[index]);
        }
    }

    // stops your own stream (Meeting only, Watch passes null)
    [RelayCommand]
    private async Task StopStream()
    {
        if (_stopStream is not null) await _stopStream();
    }

    // asks first unless "don't ask me again" was ticked
    [RelayCommand]
    private async Task Leave()
    {
        if (SkipLeaveConfirmation) await _leave();
        else DialogKind = StreamDialogKind.Leave;
    }

    // the Leave button inside the confirm dialog
    [RelayCommand]
    private async Task ConfirmLeave()
    {
        DialogKind = StreamDialogKind.None;
        await _leave();
    }

    [RelayCommand]
    private void ToggleSkipLeaveConfirmation() => SkipLeaveConfirmation = !SkipLeaveConfirmation;

    // ---- watching only ----

    // the member ring rail on the right of the Watch screen
    [ObservableProperty]
    public partial bool ShowMembers { get; set; } = true;

    // options in the resolution dropdown
    public IReadOnlyList<string> Resolutions { get; } = ["Auto", "1440p", "1080p", "720p"];

    // nothing reads this yet
    [ObservableProperty]
    public partial string SelectedResolution { get; set; } = "1440p";

    // 0-100, WatchView pushes it to the player
    public double Volume
    {
        get;
        set
        {
            if (!SetProperty(ref field, Math.Clamp(value, 0, 100))) return;

            // dragging the slider while muted should bring the sound back
            if (IsStreamMuted && field > 0) IsStreamMuted = false;
            OnPropertyChanged(nameof(IsStreamSilent));
        }
    } = 50;

    public bool IsStreamMuted
    {
        get;
        set
        {
            if (!SetProperty(ref field, value)) return;
            OnPropertyChanged(nameof(IsStreamSilent));
            OnPropertyChanged(nameof(StreamMuteLabel));
        }
    }

    // what the speaker icon shows: crossed out when muted or the slider is at zero
    public bool IsStreamSilent => IsStreamMuted || Volume == 0;

    public string StreamMuteLabel => IsStreamMuted ? "Unmute stream" : "Mute stream";

    // muting drops the slider to 0 and remembers where it was, unmuting puts it back
    [RelayCommand]
    private void ToggleStreamMute()
    {
        if (IsStreamMuted)
        {
            // flag first, otherwise the volume change below would think the user dragged the slider
            IsStreamMuted = false;
            Volume = _volumeBeforeMute;
            return;
        }

        // already at 0 (dragged there by hand)? keep the old remembered value to come back to
        if (Volume > 0) _volumeBeforeMute = Volume;
        IsStreamMuted = true;
        Volume = 0;
    }
}
