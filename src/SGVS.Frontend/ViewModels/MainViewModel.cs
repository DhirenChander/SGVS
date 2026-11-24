using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SGVS.Frontend.Services;
using SGVS.Frontend.Services.Networking;
using SGVS.Frontend.Services.Video;
using SharedMemLibrary;

namespace SGVS.Frontend.ViewModels;

// the shell. MainWindow shows whatever page this points at, the ViewLocator picks the view
public partial class MainViewModel : ViewModelBase
{
    private readonly Func<IFrameBuffer?, IMeetingService> _createMeeting;
    private IMeetingService? _meeting;

    private readonly IFrameBuffer? _sharedMemory;

    // the real app. SGVS_FAKE=1 swaps in the pretend call so the UI can be used without the networking module
    public MainViewModel(IFrameBuffer sharedMemory) : this(CreateMeeting, sharedMemory)
    {

    }

    // tests and the previewer: no native shared memory is opened
    public MainViewModel(Func<IMeetingService> createMeeting) : this(_ => createMeeting(), null)
    {
    }

    // createMeeting makes a fresh call for each trip through Start -> Meeting. pages navigate by
    // handing the next page to the callback
    public MainViewModel(Func<IFrameBuffer?, IMeetingService> createMeeting, IFrameBuffer? sharedMemory)
    {
        _createMeeting = createMeeting;
        _sharedMemory = sharedMemory;
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
        var meeting = _createMeeting(_sharedMemory);
        _meeting = meeting;
        return new StartViewModel(meeting, () => GoToMeetingPage(meeting), () => GoToStartPage());
    }

    private void GoToMeetingPage(IMeetingService meeting)
    {
        CurrentView = new MeetingViewModel(meeting, page => CurrentView = page, () => GoToStartPage());
    }

    private static IMeetingService CreateMeeting(IFrameBuffer? sharedMemory)
    {
        if (Environment.GetEnvironmentVariable("SGVS_FAKE") == "1") return new FakeMeetingService();
        if (sharedMemory is null) throw new InvalidOperationException("shared memory is not open");

        // display names aren't decided yet, so for now it's whoever is signed in to the PC
        return new NetworkMeetingService(Environment.UserName, StartNetworking,
            action => Dispatcher.UIThread.Post(action), sharedMemory,
            video: new VideoServices(
                FfmpegStreamPublisher.CreateDummy,
                () => new FfmpegVideoDecoder(Ffmpeg.Find() ?? throw new InvalidOperationException(Ffmpeg.NotFoundMessage))));
    }

    private static INetworkingProcess StartNetworking()
    {
        try
        {
            var path = NetworkingProcess.FindExecutable()
                       ?? throw new FileNotFoundException("The networking module (backend/sgvs-networking) was not found next to the app.");

            Trace.WriteLine("[networking] starting " + path);
            return new NetworkingProcess(path);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}
