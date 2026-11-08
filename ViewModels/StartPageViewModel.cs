using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace SVGS.Frontend.ViewModels;

public partial class StartPageViewModel : ViewModelBase
{
    private Action _goToJoinMeeting;
    private Action _goToMeetingPage;

    public StartPageViewModel(Action goToJoinMeeting, Action goToMeetingPage)
    {
        _goToJoinMeeting = goToJoinMeeting;
        _goToMeetingPage = goToMeetingPage;
    }

    [RelayCommand]
    private void ShowJoinMeeting()
    {
        _goToJoinMeeting?.Invoke();
    }

    [RelayCommand]
    private void ShowMeeting()
    {
        _goToMeetingPage?.Invoke();
    }
}