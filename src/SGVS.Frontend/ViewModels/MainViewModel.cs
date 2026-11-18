using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SGVS.Frontend.Services;
using SGVS.Frontend.Services.Networking;
using SharedMemLibrary;

namespace SGVS.Frontend.ViewModels;

// the shell. MainWindow shows whatever page this points at, the ViewLocator picks the view
public partial class MainViewModel : ViewModelBase
{
    private readonly Func<IMeetingService> _createMeeting;
    private IMeetingService? _meeting;

    // the real app. SGVS_FAKE=1 swaps in the pretend call so the UI can be used without the networking module
    public MainViewModel() : this(CreateMeeting)
    {
    }

    // createMeeting makes a fresh call for each trip through Start -> Meeting. pages navigate by
    // handing the next page to the callback
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
        // whatever call was in progress is over
        // TODO: tell the service once leaving is possible through shared memory
        // _ = _meeting?.LeaveAsync();
        CurrentView = NewStartPage();
    }

    // called when the window closes so the networking module doesn't outlive the app
    public void Shutdown()
    {
        // TODO: tell the service once leaving is possible through shared memory
        // _ = _meeting?.LeaveAsync();
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

    private static IMeetingService CreateMeeting()
    {
        if (Environment.GetEnvironmentVariable("SGVS_FAKE") == "1") return new FakeMeetingService();

        // display names aren't decided yet, so for now it's whoever is signed in to the PC
        return new NetworkMeetingService(Environment.UserName, StartNetworking, () => new GCFrameBuffer(),
            action => Dispatcher.UIThread.Post(action));
    }

    private static INetworkingProcess StartNetworking()
    {
        var path = NetworkingProcess.FindExecutable()
                   ?? throw new FileNotFoundException("The networking module (backend/sgvs-networking) was not found next to the app.");
        Trace.WriteLine("[networking] starting " + path);
        return new NetworkingProcess(path);
    }
}
