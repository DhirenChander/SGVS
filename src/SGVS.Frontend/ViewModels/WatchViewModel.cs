using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using SGVS.Frontend.Models;

namespace SGVS.Frontend.ViewModels;

// which options dialog (if any) is currently sitting over the stream. None = closed
public enum StreamDialogKind
{
    None,
    StartStream,
    StopWatching,
    Leave,
}

public class WatchViewModel : ViewModelBase
{
    // our own entry in the list, so the mute button can update our tile too
    private readonly Participant _self;

    private bool _isMuted;
    private string _selectedResolution = "1440p";
    private double _volume = 50;
    private bool _isStreamMuted;
    private double _volumeBeforeMute = 50;

    public WatchViewModel()
    {
        // fake people for now, swap for real call data later. colors match the four
        // avatar-ring slots in the mockup (amber streamer, teal self, cream, muted-gray)
        _self = new Participant { Name = "You", IsSelf = true, AvatarBrush = SolidBrush("#53C9C9") };
        Participants =
        [
            new Participant { Name = "Sam", IsStreamer = true, AvatarBrush = SolidBrush("#EDB25A") },
            _self,
            new Participant { Name = "Priya", AvatarBrush = SolidBrush("#F3DAB2") },
            new Participant { Name = "Jo", AvatarBrush = SolidBrush("#B4B0AC") },
        ];

        ToggleStreamMuteCommand = new RelayCommand(ToggleStreamMute);
        ToggleCodeCommand = new RelayCommand(() => IsCodeRevealed = !IsCodeRevealed);

        // neither actually switches any stream yet - there's no backend for that - but
        // they at least open the options dialog now instead of doing nothing
        StopWatchingCommand = new RelayCommand(() => DialogKind = StreamDialogKind.StopWatching);
        StartStreamCommand = new RelayCommand(() => DialogKind = StreamDialogKind.StartStream);
        CloseDialogCommand = new RelayCommand(() => DialogKind = StreamDialogKind.None);
        ToggleSkipLeaveConfirmationCommand = new RelayCommand(() => SkipLeaveConfirmation = !SkipLeaveConfirmation);

        // skips the confirm dialog entirely once "don't ask me again" is checked - still
        // no real call to leave, so either path just quits
        LeaveCommand = new RelayCommand(() =>
        {
            if (SkipLeaveConfirmation) Shutdown();
            else DialogKind = StreamDialogKind.Leave;
        });
        ConfirmLeaveCommand = new RelayCommand(Shutdown);
    }

    private static void Shutdown() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

    private static IBrush SolidBrush(string hex) => new SolidColorBrush(Color.Parse(hex));

    // placeholder frame from StreamImage/image.png, null if it's not there
    public Bitmap? StreamFrame { get; } = LoadStreamFrame();

    private static Bitmap? LoadStreamFrame() => LoadImage("StreamImage", "image.png");

    // images live in folders next to the exe, null if missing or unreadable
    private static Bitmap? LoadImage(string folder, string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, folder, file);
        if (!File.Exists(path)) return null;

        try { return new Bitmap(path); }
        catch (Exception) { return null; }
    }

    public ObservableCollection<Participant> Participants { get; }

    public IReadOnlyList<string> Resolutions { get; } = ["Auto", "1440p", "1080p", "720p"];

    // fake join code until meetings actually hand one out. split into the two lines
    // the code-reveal rail shows it as
    public string JoinCode { get; } = "KX729Q";

    public string JoinCodeFirstHalf => JoinCode[..3];

    public string JoinCodeSecondHalf => JoinCode[3..];

    private bool _isCodeRevealed;

    public bool IsCodeRevealed
    {
        get => _isCodeRevealed;
        set => SetProperty(ref _isCodeRevealed, value);
    }

    // true for the brief "Copied!" flash after clicking the code. WatchView.axaml.cs
    // flips it back off on a short timer
    private bool _isCodeCopied;

    public bool IsCodeCopied
    {
        get => _isCodeCopied;
        set => SetProperty(ref _isCodeCopied, value);
    }

    // the start-stream / stop-watching options dialog. purely UI for now - nothing here
    // actually starts or stops anything, same placeholder state the two commands were in
    private StreamDialogKind _dialogKind;

    public StreamDialogKind DialogKind
    {
        get => _dialogKind;
        set
        {
            if (!SetProperty(ref _dialogKind, value)) return;
            OnPropertyChanged(nameof(IsDialogOpen));
            OnPropertyChanged(nameof(IsStartStreamDialog));
            OnPropertyChanged(nameof(IsStopWatchingDialog));
            OnPropertyChanged(nameof(IsLeaveDialog));
            OnPropertyChanged(nameof(DialogConfirmLabel));
        }
    }

    public bool IsDialogOpen => DialogKind != StreamDialogKind.None;

    public bool IsStartStreamDialog => DialogKind == StreamDialogKind.StartStream;

    public bool IsStopWatchingDialog => DialogKind == StreamDialogKind.StopWatching;

    public bool IsLeaveDialog => DialogKind == StreamDialogKind.Leave;

    // only read by the Start stream / Stop watching dialogs - Leave has its own
    // self-contained button row since its confirm action isn't "just close the dialog"
    public string DialogConfirmLabel => DialogKind == StreamDialogKind.StopWatching ? "Stop watching" : "Start stream";

    // "don't ask me again" on the leave-confirm dialog. session-only like the rest of
    // the frontend-only prefs - no backend to persist it to yet
    private bool _skipLeaveConfirmation;

    public bool SkipLeaveConfirmation
    {
        get => _skipLeaveConfirmation;
        set => SetProperty(ref _skipLeaveConfirmation, value);
    }

    // mock source list for the start-stream dialog - nothing reads the selection yet
    public IReadOnlyList<string> StreamSources { get; } = ["Entire screen", "Application window", "Browser tab"];

    private string _selectedStreamSource = "Entire screen";

    public string SelectedStreamSource
    {
        get => _selectedStreamSource;
        set => SetProperty(ref _selectedStreamSource, value);
    }

    // frontend-only prefs from the settings menu - no backend to persist these yet, so
    // they just reset to the defaults below every launch
    private bool _showMembers = true;

    public bool ShowMembers
    {
        get => _showMembers;
        set => SetProperty(ref _showMembers, value);
    }

    private bool _showJoinCode = true;

    public bool ShowJoinCode
    {
        get => _showJoinCode;
        set => SetProperty(ref _showJoinCode, value);
    }

    // skips the idle auto-hide entirely when on - bar/gear/fullscreen just stay up
    private bool _keepControlsVisible;

    public bool KeepControlsVisible
    {
        get => _keepControlsVisible;
        set => SetProperty(ref _keepControlsVisible, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (!SetProperty(ref _isMuted, value)) return;
            _self.IsMuted = value;
            OnPropertyChanged(nameof(MuteLabel));
        }
    }

    public string MuteLabel => IsMuted ? "Unmute" : "Mute";

    // nothing reads this yet
    public string SelectedResolution
    {
        get => _selectedResolution;
        set => SetProperty(ref _selectedResolution, value);
    }

    // 0-100, WatchView pushes it to the player
    public double Volume
    {
        get => _volume;
        set
        {
            if (!SetProperty(ref _volume, Math.Clamp(value, 0, 100))) return;

            // dragging the slider while muted should bring the sound back
            if (_isStreamMuted && _volume > 0) IsStreamMuted = false;
            OnPropertyChanged(nameof(IsStreamSilent));
        }
    }

    // muting drops the slider to 0 and remembers where it was, unmuting puts it back
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

    public bool IsStreamMuted
    {
        get => _isStreamMuted;
        set
        {
            if (!SetProperty(ref _isStreamMuted, value)) return;
            OnPropertyChanged(nameof(IsStreamSilent));
            OnPropertyChanged(nameof(StreamMuteLabel));
        }
    }

    // what the speaker icon shows: crossed out when muted or the slider is at zero
    public bool IsStreamSilent => IsStreamMuted || Volume == 0;

    public string StreamMuteLabel => IsStreamMuted ? "Unmute stream" : "Mute stream";

    public ICommand ToggleStreamMuteCommand { get; }

    public ICommand ToggleCodeCommand { get; }

    public ICommand StopWatchingCommand { get; }

    public ICommand StartStreamCommand { get; }

    public ICommand CloseDialogCommand { get; }

    public ICommand ToggleSkipLeaveConfirmationCommand { get; }

    public ICommand LeaveCommand { get; }

    public ICommand ConfirmLeaveCommand { get; }
}
