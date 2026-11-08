using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace SVGS.Frontend.ViewModels;

public partial class JoinMeetingViewModel : ViewModelBase
{
    private Action _goToStartPage;

    public JoinMeetingViewModel(Action goToStartPage)
    {
        _goToStartPage = goToStartPage;
    }

    [RelayCommand]
    private void GoToStartPage()
    {
        _goToStartPage?.Invoke();
    }
}
