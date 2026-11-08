using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace SVGS.Frontend.Views;

public partial class MeetingView : UserControl
{
    // Temporary value. Later, use the code supplied by the active meeting.
    private const string DemoMeetingCode = "SGVS-DEMO";

    public MeetingView()
    {
        InitializeComponent();
        //CopyCodeButton.Content = DemoMeetingCode;
    }

    private void WatchStream_Click(object? sender, RoutedEventArgs e)
    {
        ShareOptions.IsVisible = false;
        StreamStatusText.Text = "Watch stream selected (demo).";
        // Later: navigate to WatchView with the selected stream.
    }

    private void StartStream_Click(object? sender, RoutedEventArgs e)
    {
        ShareOptions.IsVisible = true;
        StreamStatusText.Text = "Choose a screen or an app to share.";
    }

    private void ShareScreen_Click(object? sender, RoutedEventArgs e)
    {
        ShareOptions.IsVisible = false;
        StreamStatusText.Text = "Screen sharing selected (demo).";
        // Later: open the screen picker and start capture.
    }

    private void ShareApp_Click(object? sender, RoutedEventArgs e)
    {
        ShareOptions.IsVisible = false;
        StreamStatusText.Text = "App sharing selected (demo).";
        // Later: open the app picker and start capture.
    }

    private void CancelShare_Click(object? sender, RoutedEventArgs e)
    {
        ShareOptions.IsVisible = false;
        StreamStatusText.Text = "Sharing cancelled.";
    }

    private void RevealCode_Click(object? sender, RoutedEventArgs e)
    {
        CodePanel.IsVisible = !CodePanel.IsVisible;
        RevealCodeButton.Content = CodePanel.IsVisible ? "Hide code" : "Reveal code";
        CodeFeedbackText.Text = "Click the code to copy it. This is a demo code.";
    }

    private async void CopyCode_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            CodeFeedbackText.Text = "Clipboard unavailable. Try copying from the running app.";
            return;
        }

        CopyCodeButton.IsEnabled = false;
        try
        {
            await clipboard.SetTextAsync(DemoMeetingCode);
            CodeFeedbackText.Text = "Copied! This is a demo code.";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            CodeFeedbackText.Text = "Couldn't copy the code. Please try again.";
        }
        finally
        {
            CopyCodeButton.IsEnabled = true;
        }
    }
    private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
	}
}

