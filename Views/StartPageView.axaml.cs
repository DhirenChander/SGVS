using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;

namespace SVGS.Frontend.Views;

public partial class StartPageView : UserControl
{
	public StartPageView()
	{
		InitializeComponent();
	}
    

    private void CreateMeetingButton_OnClick(object? sender, RoutedEventArgs e)
    {
        //CreateMeetingButton.Classes.Set("clicked", true);
        //CreateMeetingButton.Content = "End Meeting";
        //CreateMeetingLink.Text = "https://example.com/meeting";

    }

    private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
	}
}
