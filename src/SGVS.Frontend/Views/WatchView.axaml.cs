using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using SGVS.Frontend.ViewModels;

namespace SGVS.Frontend.Views;

public partial class WatchView : UserControl
{
    // control chrome fades out after this long with the mouse sitting still over the
    // stream, same idea as youtube/twitch - comes back on the next move
    private static readonly TimeSpan HideDelay = TimeSpan.FromSeconds(2.5);
    private readonly DispatcherTimer _hideTimer = new() { Interval = HideDelay };

    // how long the "Copied!" swap-in sticks around after clicking the join code
    private readonly DispatcherTimer _copiedResetTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };

    private Window? _window;
    private bool _isFullscreen;
    private Thickness _streamMarginRestore;

    // true while the resolution flyout is open - the hide timer still ticks (simpler
    // than stopping/restarting it) but this stops it from actually hiding anything
    private bool _suppressAutoHide;

    public WatchView()
    {
        InitializeComponent();

        _hideTimer.Tick += (_, _) =>
        {
            // a popup list or an open dialog both live outside ChromeGroup (see
            // StreamOverlay.axaml), so neither one feeds this timer by itself - without
            // this check they'd get hidden out from under the user mid-interaction.
            // KeepControlsVisible is the settings-menu toggle for "just never hide these"
            if (_suppressAutoHide || DataContext is WatchViewModel { IsDialogOpen: true } or WatchViewModel { KeepControlsVisible: true })
                return;
            Overlay.ControlsVisible = false;
            _hideTimer.Stop();
        };
        StreamHost.PointerMoved += (_, _) => ShowControlsForAWhile();

        Overlay.FlyoutOpened += (_, _) => _suppressAutoHide = true;
        Overlay.FlyoutClosed += (_, _) =>
        {
            _suppressAutoHide = false;
            ShowControlsForAWhile(); // fresh countdown instead of hiding right away
        };

        _copiedResetTimer.Tick += (_, _) =>
        {
            if (DataContext is WatchViewModel vm) vm.IsCodeCopied = false;
            _copiedResetTimer.Stop();
        };

        // closing the dialog (however it closed) deserves the same fresh countdown, and
        // the members/code rails need to react to their settings-menu toggles
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not WatchViewModel vm) return;
            UpdateMembersRailVisibility();
            UpdateCodeRailVisibility();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(WatchViewModel.DialogKind) && !vm.IsDialogOpen)
                    ShowControlsForAWhile();
                if (e.PropertyName == nameof(WatchViewModel.ShowMembers)) UpdateMembersRailVisibility();
                if (e.PropertyName == nameof(WatchViewModel.ShowJoinCode)) UpdateCodeRailVisibility();
            };
        };

        Loaded += (_, _) => ShowControlsForAWhile();
        Unloaded += (_, _) =>
        {
            _hideTimer.Stop();
            _copiedResetTimer.Stop();
        };
    }

    private void ShowControlsForAWhile()
    {
        Overlay.ControlsVisible = true;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void OnFullscreenRequested(object? sender, EventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (_window is null)
        {
            _window = TopLevel.GetTopLevel(this) as Window;
            if (_window is null) return;

            // esc exits fullscreen same as everywhere else
            _window.KeyDown += (_, args) =>
            {
                if (_isFullscreen && args.Key == Key.Escape) ToggleFullscreen();
            };
        }

        _isFullscreen = !_isFullscreen;
        _window.WindowState = _isFullscreen ? WindowState.FullScreen : WindowState.Normal;

        if (_isFullscreen)
        {
            // remember the real margin rather than hardcoding it twice
            _streamMarginRestore = StreamBorder.Margin;
            StreamBorder.Margin = new Thickness(0);
            StreamBorder.CornerRadius = new CornerRadius(0);
        }
        else
        {
            StreamBorder.Margin = _streamMarginRestore;
            StreamBorder.CornerRadius = new CornerRadius(8);
        }

        UpdateMembersRailVisibility();
        UpdateCodeRailVisibility();
    }

    // visible when not fullscreen AND the settings-menu toggle for it is on - either one
    // can hide it, so both need to go through here rather than setting IsVisible directly
    private void UpdateMembersRailVisibility() =>
        MembersRail.IsVisible = !_isFullscreen && DataContext is not WatchViewModel { ShowMembers: false };

    private void UpdateCodeRailVisibility() =>
        CodeRail.IsVisible = !_isFullscreen && DataContext is not WatchViewModel { ShowJoinCode: false };

    // clicking the revealed code copies it, with a brief "Copied!" swap-in for feedback
    private async void OnCodeTextPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not WatchViewModel vm) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        await clipboard.SetTextAsync(vm.JoinCode);
        vm.IsCodeCopied = true;
        _copiedResetTimer.Stop();
        _copiedResetTimer.Start();
    }

    // the button under the revealed code hides it again, back to just the key icon
    private void OnHideCodeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not WatchViewModel vm) return;
        vm.IsCodeRevealed = false;
        vm.IsCodeCopied = false;
        _copiedResetTimer.Stop();
    }
}
