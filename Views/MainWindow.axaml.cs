using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Diagnostics;
using Avalonia.Markup.Xaml;

namespace SVGS.Frontend.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // does nothing right now
    private bool tryFindMeeting(string meetingLink)
    {
        // uses the networking library to see if the meeting exists
        return false;
    }

    // does nothing right now
    private void joinMeeting(string meetingLink)
    {
        // attempts to join the meeting
        bool meetingFound = false;
        int joinFails = 0;
        if (meetingFound)
        {
            // join the meeting
        }
        else
        {
            joinFails++;
            if (joinFails >= 3)
            {
                throw new Exception("Failed to join meeting after 3 attempts");
            }
        }
    }

    private string createMeeting()
    {
        // create a new meeting and return the link
        return "https://example.com/meeting";
    }

    private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
	}
}
