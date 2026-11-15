using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SGVS.Frontend.ViewModels;
using SGVS.Frontend.Views;

namespace SGVS.Frontend;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var main = new MainViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = main,
            };

            // the networking module is a child process, don't leave it running
            desktop.ShutdownRequested += (_, _) => main.Shutdown();
        }

        base.OnFrameworkInitializationCompleted();
    }
}