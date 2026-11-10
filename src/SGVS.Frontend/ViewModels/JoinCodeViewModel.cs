using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SGVS.Frontend.ViewModels;

// state for the join-code rail (JoinCodeRail), shared by the Meeting and Watch pages:
// a key icon that expands into the meeting code, click the code to copy it
public partial class JoinCodeViewModel : ObservableObject
{
    // what gets copied, dash and all (KX7-29Q)
    public JoinCodeViewModel(string code)
    {
        Code = code;
    }

    // for the XAML previewer only
    public JoinCodeViewModel() : this("KX7-29Q")
    {
    }

    public string Code { get; }

    // the rail shows the code over two lines, so it's the code without the dash split in half
    public string FirstHalf => Code.Replace("-", "")[..3];

    public string SecondHalf => Code.Replace("-", "")[3..];

    // key icon swapped for the code
    [ObservableProperty]
    public partial bool IsRevealed { get; set; }

    // true for the brief "Copied!" flash after clicking the code. JoinCodeRail turns it back off
    [ObservableProperty]
    public partial bool IsCopied { get; set; }

    [RelayCommand]
    private void ToggleReveal() => IsRevealed = !IsRevealed;
}
