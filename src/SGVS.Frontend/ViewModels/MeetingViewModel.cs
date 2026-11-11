using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SGVS.Frontend.Models;
using SGVS.Frontend.Services;

namespace SGVS.Frontend.ViewModels;

// in a call: everyone's tile in a gallery, start/stop your own stream, watch someone else's. shown by MeetingView
public partial class MeetingViewModel : ViewModelBase
{
    private readonly IMeetingService _meeting;
    private readonly Action<ViewModelBase> _navigate;
    private readonly Action _goToStart;

    // for the XAML previewer only
    public MeetingViewModel() : this(new FakeMeetingService(), _ => { }, () => { })
    {
    }

    // navigate swaps the page MainWindow shows (see MainViewModel). goToStart is what Leave does after the call ends
    public MeetingViewModel(IMeetingService meeting, Action<ViewModelBase> navigate, Action goToStart)
    {
        _meeting = meeting;
        _navigate = navigate;
        _goToStart = goToStart;

        // ReadOnlyObservableCollection only exposes the event through the interface
        ((INotifyCollectionChanged)Members).CollectionChanged += (_, _) => OnPropertyChanged(nameof(GalleryColumns));
        _meeting.Self.PropertyChanged += OnSelfChanged;

        JoinCode = new JoinCodeViewModel(_meeting.Code);

        // control bar + gear. Meeting has no viewer controls, Start stream and Leave act on the call
        Overlay = new OverlayViewModel(_meeting.Self, showViewerControls: false,
            startStream: _meeting.StartStreamAsync, stopStream: _meeting.StopStreamAsync, leave: LeaveMeeting,
            stopWatching: null)
        {
            IsStreaming = _meeting.Self.IsStreamer,
        };
    }

    // bar, gear and dialogs drawn by StreamOverlay
    public OverlayViewModel Overlay { get; }

    // the key icon / code rail at the bottom-right
    public JoinCodeViewModel JoinCode { get; }

    public ReadOnlyObservableCollection<Participant> Members => _meeting.Members;

    // 1 person fills the screen, 2-4 is 2x2 like the mockup, then 3 and 4 across
    public int GalleryColumns => Members.Count switch
    {
        <= 1 => 1,
        <= 4 => 2,
        <= 9 => 3,
        _ => 4,
    };

    // opens their stream. Stop watching on that page comes back to this same page
    [RelayCommand]
    private void Watch(Participant member) =>
        _navigate(new WatchViewModel(member, _meeting, () => _navigate(this), _goToStart));

    // what the overlay's Leave calls once confirmed. tells the service, then goes back to the Start screen
    private async Task LeaveMeeting()
    {
        await _meeting.LeaveAsync();
        _goToStart();
    }

    private void OnSelfChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Participant.IsStreamer))
            Overlay.IsStreaming = _meeting.Self.IsStreamer;
    }
}
