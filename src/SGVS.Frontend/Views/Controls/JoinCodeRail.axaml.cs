using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SGVS.Frontend.ViewModels;

namespace SGVS.Frontend.Views.Controls;

public partial class JoinCodeRail : UserControl
{
    // how long the "Copied!" swap-in sticks around after clicking the code
    private readonly DispatcherTimer _copiedResetTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };

    public JoinCodeRail()
    {
        InitializeComponent();

        _copiedResetTimer.Tick += (_, _) =>
        {
            if (DataContext is JoinCodeViewModel vm) vm.IsCopied = false;
            _copiedResetTimer.Stop();
        };
        Unloaded += (_, _) => _copiedResetTimer.Stop();
    }

    // clicking the revealed code copies it
    private async void OnCodePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not JoinCodeViewModel vm) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        await clipboard.SetTextAsync(vm.Code);
        vm.IsCopied = true;

        // clicking again during the flash restarts it
        _copiedResetTimer.Stop();
        _copiedResetTimer.Start();
    }

    // the button under the code hides it again, back to just the key icon
    private void OnHideClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not JoinCodeViewModel vm) return;
        vm.IsRevealed = false;
        vm.IsCopied = false;
        _copiedResetTimer.Stop();
    }
}
