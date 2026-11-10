using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SGVS.Frontend.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    // Leave closes the whole app for now, there's no Start screen to go back to yet
    protected static void Shutdown() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
}
