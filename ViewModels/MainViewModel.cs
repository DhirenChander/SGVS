using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SVGS.Frontend.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    // [ObservableProperty]
    // private ObservableObject _currentPage;

    // public MainViewModel()
    // {
    //     var startViewModel = new StartViewModel();
    //     startViewModel.NavigateToJoinMeeting = () => CurrentPage = new JoinMeetingViewModel();
    //     _currentPage = startViewModel;
    // }

    [ObservableProperty]
    private ViewModelBase _currentPage;

    public MainViewModel()
    {
        CurrentPage = new StartPageViewModel(() => GoToJoinMeeting(), () => GoToMeetingPage());
    }

    [RelayCommand]
    private void GoToJoinMeeting()
    {
        CurrentPage = new JoinMeetingViewModel(() => GoToStartPage());
    }

    [RelayCommand]
    private void GoToStartPage()
    {
        CurrentPage = new StartPageViewModel(() => GoToJoinMeeting(), () => GoToMeetingPage());
    }

    private void GoToMeetingPage()
    {
        CurrentPage = new MeetingViewModel(() => GoToWatchView());
    }

    private void GoToWatchView()
    {
        CurrentPage = new WatchViewModel(() => GoToMeetingPage());
    }
}
