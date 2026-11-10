using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SGVS.Frontend.ViewModels;

namespace SGVS.Frontend.Views;

public partial class MeetingView : UserControl
{
    public MeetingView()
    {
        InitializeComponent();

        // tunnel + handledEventsToo so this sees every click first, even on buttons
        AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnAnyKeyDown, RoutingStrategies.Tunnel);
    }

    // clicking anywhere else closes the stream menu and hides the code
    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MeetingViewModel vm) return;
        var target = e.Source as Visual;

        if (vm.IsCodeRevealed && !IsWithin(target, CodeReveal))
            vm.HideCode();

        // the button toggles the menu itself, closing here too would reopen it
        if (vm.IsShareMenuOpen && !IsWithin(target, StreamMenu) && !IsWithin(target, StartStreamButton))
            vm.CloseShareMenu();
    }

    private void OnAnyKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not MeetingViewModel vm) return;
        if (!vm.IsShareMenuOpen && !vm.IsCodeRevealed) return;

        vm.CloseShareMenu();
        vm.HideCode();
        e.Handled = true;
    }

    private static bool IsWithin(Visual? visual, Visual container) =>
        visual is not null && (visual == container || container.IsVisualAncestorOf(visual));
}
