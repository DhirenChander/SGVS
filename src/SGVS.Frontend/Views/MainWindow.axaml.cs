using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SGVS.Frontend.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // on Windows, extending into the decorations makes the OS paint its own
        // min/max/close buttons in the top-right, right on top of ours. BorderOnly
        // drops the native title bar (keeps the resize border), so only our bar shows.
        // other platforms are left as they were
        if (OperatingSystem.IsWindows())
        {
            ExtendClientAreaToDecorationsHint = false;
            WindowDecorations = WindowDecorations.BorderOnly;
        }
    }

    // drag-to-move + double-click-to-maximize, same as a native title bar. only fires
    // when the press landed on the bar itself, not on one of the three buttons sitting
    // inside it (those get their own Click handlers below)
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender)) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        BeginMoveDrag(e);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
