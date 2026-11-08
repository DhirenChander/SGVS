using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace SVGS.Frontend.ViewModels;

public partial class MeetingViewModel : ViewModelBase
{
    private Action _goToScreenShare;

    public MeetingViewModel(Action goToScreenShare)
    {
        _goToScreenShare = goToScreenShare;
    }

    [RelayCommand]
    private void GoToScreenShare()
    {
        _goToScreenShare?.Invoke();
    }
}