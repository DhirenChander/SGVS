using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SVGS.Frontend.Views;

public partial class JoinMeetingView : UserControl
{
	public JoinMeetingView()
	{
		InitializeComponent();
	}

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
	}
}
