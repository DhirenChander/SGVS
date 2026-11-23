using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using SGVS.Frontend.ViewModels;

namespace SGVS.Frontend.Views;

public partial class StartView : UserControl
{
    public StartView()
    {
        InitializeComponent();
    }

    // puts the reply for the host on the clipboard
    private async void OnCopyReplyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StartViewModel vm) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        await clipboard.SetTextAsync(vm.ReplyCode);
    }
}
