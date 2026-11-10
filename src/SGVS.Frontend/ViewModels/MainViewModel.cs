using CommunityToolkit.Mvvm.ComponentModel;
using SGVS.Frontend.Services;

namespace SGVS.Frontend.ViewModels;

// the shell. MainWindow shows whatever page this points at, the ViewLocator picks the view
public partial class MainViewModel : ViewModelBase
{
    // straight into a pretend meeting until the Start screen can create/join one.
    // pages navigate by handing the next page to the callback
    public MainViewModel()
    {
        CurrentView = new MeetingViewModel(new FakeMeetingService(), page => CurrentView = page);
    }

    [ObservableProperty]
    public partial ViewModelBase CurrentView { get; set; }
}
