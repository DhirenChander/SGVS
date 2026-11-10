using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SGVS.Frontend.ViewModels;

namespace SGVS.Frontend.Views;

public partial class WatchView : UserControl
{
    private Window? _window;
    private bool _isFullscreen;
    private Thickness _streamMarginRestore;

    public WatchView()
    {
        InitializeComponent();

        // moving the mouse over the stream brings the overlay back, it hides itself when idle
        StreamHost.PointerMoved += (_, _) => Overlay.NotifyActivity();

        // the members/code rails react to their settings-menu toggles
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not WatchViewModel vm) return;
            UpdateMembersRailVisibility();
            UpdateCodeRailVisibility();
            vm.Overlay.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(OverlayViewModel.ShowMembers)) UpdateMembersRailVisibility();
                if (e.PropertyName == nameof(OverlayViewModel.ShowJoinCode)) UpdateCodeRailVisibility();
            };
        };
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
        MembersRail.IsVisible = !_isFullscreen && DataContext is not WatchViewModel { Overlay.ShowMembers: false };

    private void UpdateCodeRailVisibility() =>
        CodeRail.IsVisible = !_isFullscreen && DataContext is not WatchViewModel { Overlay.ShowJoinCode: false };
}
