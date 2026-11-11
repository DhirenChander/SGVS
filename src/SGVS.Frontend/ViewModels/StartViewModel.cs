using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace SGVS.Frontend.ViewModels;

// home screen: create or join a room. shown by StartView
public partial class StartViewModel : ViewModelBase
{
    private Action _goToMeetingPage;

    public StartViewModel(Action goToMeetingPage)
    {
        _goToMeetingPage = goToMeetingPage;
    }

    [RelayCommand]
    private void ShowMeeting()
    {
        _goToMeetingPage?.Invoke();
    }
}
