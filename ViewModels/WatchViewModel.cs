using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using SVGS.Frontend.Models;

namespace SVGS.Frontend.ViewModels;

public class WatchViewModel : ViewModelBase
{
    // our own entry in the list, so the mute button can update our tile too
    private readonly Participant _self;

    private bool _isMuted;
    private bool _isVideoOn = true;
    private string _selectedResolution = "Auto";
    private double _volume = 50;
    private bool _isStreamMuted;
    private double _volumeBeforeMute = 50;

    public WatchViewModel(Action? goToMeetingPage = null)
    {
        // fake people for now, swap for real call data later
        _self = new Participant { Name = "Viewer 1", IsSelf = true, AvatarBrush = SolidBrush("#0A84FF") };
        Participants =
        [
            new Participant { Name = "Streamer", IsStreamer = true, AvatarBrush = SolidBrush("#FF9F0A") },
            _self,
            new Participant { Name = "Viewer 2", AvatarBrush = SolidBrush("#30B0C7") },
            new Participant { Name = "Viewer 3", IsMuted = true, AvatarBrush = SolidBrush("#A2845E") },
            new Participant { Name = "Viewer 4", AvatarBrush = SolidBrush("#8E8E93") },
        ];

        // a viewer's camera is "on" if ViewerCamera/<their name>.jpg exists
        foreach (var p in Participants.Where(p => !p.IsSelf && !p.IsStreamer))
            p.VideoFrame = LoadCameraFrame(p.Name);

        ToggleStreamMuteCommand = new RelayCommand(ToggleStreamMute);

        LeaveCommand = new RelayCommand(() =>
        {
            if (goToMeetingPage is not null)
            {
                goToMeetingPage();
                return;
            }

            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        });
    }

    private static IBrush SolidBrush(string hex) => new SolidColorBrush(Color.Parse(hex));

    // placeholder frame from StreamImage/image.png, null if it's not there
    public Bitmap? StreamFrame { get; } = LoadStreamFrame();

    private static Bitmap? LoadStreamFrame() => LoadImage("StreamImage", "image.png");

    private static Bitmap? LoadCameraFrame(string name) => LoadImage("ViewerCamera", name + ".jpg");

    // images live in folders next to the exe, null if missing or unreadable
    private static Bitmap? LoadImage(string folder, string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, folder, file);
        if (!File.Exists(path)) return null;

        try { return new Bitmap(path); }
        catch (Exception) { return null; }
    }

    public ObservableCollection<Participant> Participants { get; }

    public IReadOnlyList<string> Resolutions { get; } = ["Auto", "1080p", "720p", "480p"];

    public string StreamerName => Participants.First(p => p.IsStreamer).Name;

    public string ParticipantsHeader => $"In call ({Participants.Count})";

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

    public bool IsVideoOn
    {
        get => _isVideoOn;
        set
        {
            if (!SetProperty(ref _isVideoOn, value)) return;
            OnPropertyChanged(nameof(IsVideoOff));
            OnPropertyChanged(nameof(VideoLabel));
        }
    }

    // the camera button is "checked = off" like the mute one, so it binds to this
    public bool IsVideoOff
    {
        get => !_isVideoOn;
        set => IsVideoOn = !value;
    }

    public string MuteLabel => IsMuted ? "Unmute" : "Mute";

    public string VideoLabel => IsVideoOn ? "Stop video" : "Start video";

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

    public ICommand LeaveCommand { get; }
}