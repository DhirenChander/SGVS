using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace SGVS.Frontend.Services;

// keeps view models away from TopLevel so they stay testable
public interface IClipboardService
{
    // false if there's no clipboard or the copy failed
    Task<bool> TrySetTextAsync(string text);
}

// copies through the main window's clipboard
public sealed class ClipboardService : IClipboardService
{
    public async Task<bool> TrySetTextAsync(string text)
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window?.Clipboard is not { } clipboard) return false;

        try
        {
            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            return false;
        }
    }
}
