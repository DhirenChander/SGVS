using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SGVS.Frontend.Services;
using SGVS.Frontend.Services.Video;

namespace SGVS.Frontend.ViewModels;

// the shell. MainWindow shows whatever page this points at, the ViewLocator picks the view
public partial class MainViewModel : ViewModelBase
{
    private readonly Func<IMeetingService> _createMeeting;
    private IMeetingService? _meeting;

    // createMeeting makes a fresh call for each trip through Start -> Meeting (App.CreateMeeting in the app,
    // fakes in tests and the previewer). pages navigate by handing the next page to the callback
    public MainViewModel(Func<IMeetingService> createMeeting)
    {
        _createMeeting = createMeeting;
        CurrentView = NewStartPage();
    }

    [ObservableProperty]
    public partial ViewModelBase CurrentView { get; set; }

    [RelayCommand]
    private void GoToStartPage()
    {
        // whatever call was in progress is over. disposing stops its ffmpeg processes, which would
        // otherwise keep streaming from a meeting nobody is looking at
        // TODO: tell the service once leaving is possible through shared memory
        // _ = _meeting?.LeaveAsync();
        _meeting?.Dispose();
        CurrentView = NewStartPage();
    }

    // called when the window closes so the networking module doesn't outlive the app
    public void Shutdown()
    {
        // TODO: tell the service once leaving is possible through shared memory
        // _ = _meeting?.LeaveAsync();
        _meeting?.Dispose();
    }

    public void NotifyParticipantJoined(string participantName)
    {
        _meeting?.OnParticipantJoined(participantName);
    }

    // someone else's stream started arriving (see App.StartReceiver)
    public void AttachRemoteVideo(IEncodedVideoSource source)
    {
        _meeting?.AttachRemoteVideo(source);
    }

    private StartViewModel NewStartPage()
    {
        var meeting = _createMeeting();
        _meeting = meeting;
        return new StartViewModel(meeting, () => GoToMeetingPage(meeting), () => GoToStartPage());
    }

    private void GoToMeetingPage(IMeetingService meeting)
    {
        CurrentView = new MeetingViewModel(meeting, page => CurrentView = page, () => GoToStartPage());
    }
}
