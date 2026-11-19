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

    // the rail shows a short preview of the code over two lines. real connection strings are far too long
    // for that, so it only previews the first 6 characters, clicking still copies the whole thing
    private string Preview
    {
        get
        {
            var compact = Code.Replace("-", "");
            return compact.Length > 6 ? compact[..6] : compact;
        }
    }

    public string FirstHalf => Preview[..Math.Min(3, Preview.Length)];

    public string SecondHalf => Preview.Length > 3 ? Preview[3..] : "";

    // key icon swapped for the code
    [ObservableProperty]
    public partial bool IsRevealed { get; set; }

    // true for the brief "Copied!" flash after clicking the code. JoinCodeRail turns it back off
    [ObservableProperty]
    public partial bool IsCopied { get; set; }

    [RelayCommand]
    private void ToggleReveal() => IsRevealed = !IsRevealed;
}
