using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SGVS.Frontend.ViewModels;
// System.IO.Path (implicit usings) and Avalonia.Controls.Shapes.Path collide on the bare
// name "Path" - this file only ever means the shape, so alias it rather than qualify every use
using Path = Avalonia.Controls.Shapes.Path;

namespace SGVS.Frontend.Views.Controls;

public partial class StreamOverlay : UserControl
{
    // control chrome fades out after this long with the mouse sitting still, same idea as
    // youtube/twitch - comes back on the next move
    private static readonly TimeSpan HideDelay = TimeSpan.FromSeconds(2.5);
    private readonly DispatcherTimer _hideTimer = new() { Interval = HideDelay };

    // true while a flyout is open - the timer still ticks but this stops it hiding anything
    private bool _suppressAutoHide;

    public StreamOverlay()
    {
        InitializeComponent();

        _hideTimer.Tick += (_, _) =>
        {
            // flyouts are popups and the dialog lives outside ChromeGroup, so neither feeds the
            // pointer movement that restarts this timer. KeepControlsVisible is the settings toggle
            if (_suppressAutoHide || DataContext is OverlayViewModel { IsDialogOpen: true } or OverlayViewModel { KeepControlsVisible: true })
                return;
            ControlsVisible = false;
            _hideTimer.Stop();
        };
        Loaded += (_, _) => NotifyActivity();
        Unloaded += (_, _) => _hideTimer.Stop();

        // the resolution menu and the stream-source list are both built here rather than
        // from an ItemsControl - see the comment above ResolutionMenuItems in the axaml
        // for why
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not OverlayViewModel vm) return;
            RebuildResolutionRows(vm);
            RebuildStreamSourceOptions(vm);
            RebuildSettingsOptions(vm);
            vm.PropertyChanged += OnViewModelPropertyChanged;
            NotifyActivity();
        };

        // both flyouts are popups, not children of ChromeGroup, so hovering one doesn't
        // count as pointer movement for the auto-hide timer above. without this, moving the
        // mouse onto an open list makes the bar it's attached to vanish out from under it.
        // simplest fix: while either is open, don't hide anything
        ResolutionButton.Flyout!.Opened += (_, _) => _suppressAutoHide = true;
        ResolutionButton.Flyout!.Closed += (_, _) => OnFlyoutClosed();
        GearButton.Flyout!.Opened += (_, _) => _suppressAutoHide = true;
        GearButton.Flyout!.Closed += (_, _) => OnFlyoutClosed();
    }

    // lets WatchView hide/show the chrome for the auto-hide behaviour without needing a
    // bindable property just for that. ChromeGroup, not OverlayRoot - the stream-options
    // dialog lives in OverlayRoot too and must NOT disappear on the idle timer
    public bool ControlsVisible
    {
        get => ChromeGroup.IsVisible;
        set => ChromeGroup.IsVisible = value;
    }

    public event EventHandler? FullscreenRequested;

    // hosts call this on pointer movement: shows the controls and restarts the idle countdown
    public void NotifyActivity()
    {
        ControlsVisible = true;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void OnFlyoutClosed()
    {
        _suppressAutoHide = false;
        NotifyActivity(); // fresh countdown instead of hiding right away
    }

    private void OnFullscreenClick(object? sender, RoutedEventArgs e) => FullscreenRequested?.Invoke(this, EventArgs.Empty);

    private void OnDialogScrimPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (DataContext is OverlayViewModel vm) vm.CloseDialogCommand.Execute(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not OverlayViewModel vm) return;
        if (e.PropertyName == nameof(OverlayViewModel.SelectedResolution)) RefreshResolutionChecks(vm);
        if (e.PropertyName == nameof(OverlayViewModel.SelectedStreamSource)) RefreshStreamSourceChecks(vm);

        // closing a dialog or turning "keep visible" off deserves a fresh countdown too
        if (e.PropertyName == nameof(OverlayViewModel.DialogKind) && !vm.IsDialogOpen) NotifyActivity();
        if (e.PropertyName == nameof(OverlayViewModel.KeepControlsVisible) && !vm.KeepControlsVisible) NotifyActivity();
    }

    // mock source list for the start-stream dialog - same "radio row built in code"
    // pattern as the resolution menu, just rendered inline instead of in a flyout
    private void RebuildStreamSourceOptions(OverlayViewModel vm)
    {
        StreamSourceOptions.Children.Clear();

        foreach (var source in vm.StreamSources)
        {
            var radio = new RadioButton
            {
                GroupName = "StreamSource",
                Content = source,
                Tag = source,
                IsChecked = source == vm.SelectedStreamSource,
            };
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true) vm.SelectedStreamSource = source;
            };
            StreamSourceOptions.Children.Add(radio);
        }
    }

    private void RefreshStreamSourceChecks(OverlayViewModel vm)
    {
        foreach (var child in StreamSourceOptions.Children)
        {
            if (child is RadioButton { Tag: string source } radio)
                radio.IsChecked = source == vm.SelectedStreamSource;
        }
    }

    private void RebuildResolutionRows(OverlayViewModel vm)
    {
        ResolutionMenuItems.Children.Clear();

        foreach (var resolution in vm.Resolutions)
        {
            var label = new TextBlock { Text = resolution, FontSize = 14, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var check = new Path
            {
                Data = (StreamGeometry)this.FindResource("SgvsCheckIcon")!,
                Classes = { "icon" },
                Stroke = (IBrush)this.FindResource("SgvsAccentBrush")!,
                Width = 14,
                Height = 14,
                IsVisible = false,
            };

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(label);
            Grid.SetColumn(check, 1);
            row.Children.Add(check);

            var button = new Button { Classes = { "resrow" }, Content = row, Tag = resolution };
            button.Click += (_, _) =>
            {
                vm.SelectedResolution = resolution;
                FlyoutBase.GetAttachedFlyout(ResolutionButton)?.Hide();
            };

            ResolutionMenuItems.Children.Add(button);
        }

        RefreshResolutionChecks(vm);
    }

    // flips the checkmark, text color and the standing teal tint on whichever row
    // matches SelectedResolution
    private void RefreshResolutionChecks(OverlayViewModel vm)
    {
        foreach (var child in ResolutionMenuItems.Children)
        {
            if (child is not Button button || button.Content is not Grid row || button.Tag is not string resolution) continue;
            var isSelected = resolution == vm.SelectedResolution;

            button.Classes.Set("selected", isSelected);
            if (row.Children[0] is TextBlock label)
                label.Foreground = (IBrush)this.FindResource(isSelected ? "SgvsAccentBrush" : "SgvsTextBrush")!;
            if (row.Children[1] is Path check)
                check.IsVisible = isSelected;
        }
    }

    // frontend-only prefs - no backend to persist these yet, see OverlayViewModel. built
    // the same way as the resolution/source rows above: a plain resrow Button per entry,
    // this time with a hand-built toggle pill instead of a checkmark on the right
    private void RebuildSettingsOptions(OverlayViewModel vm)
    {
        SettingsOptions.Children.Clear();
        // the member rail only exists on the Watch screen
        if (vm.ShowViewerControls)
            SettingsOptions.Children.Add(BuildSettingsToggleRow("Show members", vm.ShowMembers, v => vm.ShowMembers = v));
        SettingsOptions.Children.Add(BuildSettingsToggleRow("Show join code", vm.ShowJoinCode, v => vm.ShowJoinCode = v));
        SettingsOptions.Children.Add(BuildSettingsToggleRow("Keep controls visible", vm.KeepControlsVisible, v => vm.KeepControlsVisible = v));
    }

    private Button BuildSettingsToggleRow(string label, bool initialValue, Action<bool> setValue)
    {
        var track = new Border { Classes = { "toggletrack" } };
        var knob = new Ellipse { Classes = { "toggleknob" } };
        var toggleHost = new Panel { Width = 36, Height = 20, Children = { track, knob } };
        ApplyToggleVisual(track, knob, initialValue);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(toggleHost, 1);
        row.Children.Add(toggleHost);

        var button = new Button { Classes = { "resrow" }, Content = row };
        var isOn = initialValue;
        button.Click += (_, _) =>
        {
            isOn = !isOn;
            setValue(isOn);
            ApplyToggleVisual(track, knob, isOn);
        };
        return button;
    }

    private void ApplyToggleVisual(Border track, Ellipse knob, bool isOn)
    {
        track.Background = (IBrush)this.FindResource(isOn ? "SgvsAccentBrush" : "SgvsToggleOffBrush")!;
        knob.Margin = new Thickness(isOn ? 18 : 2, 0, 0, 0);
    }
}
