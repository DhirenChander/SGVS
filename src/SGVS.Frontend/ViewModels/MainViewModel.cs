using CommunityToolkit.Mvvm.ComponentModel;

namespace SGVS.Frontend.ViewModels;

// the shell. MainWindow shows whatever page this points at, the ViewLocator picks the view
public partial class MainViewModel : ViewModelBase
{
    // straight to the watch page until Start/Meeting exist
    [ObservableProperty]
    public partial ViewModelBase CurrentView { get; set; } = new WatchViewModel();
}
