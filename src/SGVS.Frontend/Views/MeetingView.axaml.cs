using Avalonia.Controls;
using SGVS.Frontend.ViewModels;

namespace SGVS.Frontend.Views;

public partial class MeetingView : UserControl
{
    public MeetingView()
    {
        InitializeComponent();

        // any mouse movement brings the control bar back for a while
        PointerMoved += (_, _) => Overlay.NotifyActivity();
    }
}
