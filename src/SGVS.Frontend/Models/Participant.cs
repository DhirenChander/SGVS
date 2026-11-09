using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SGVS.Frontend.Models;

public class Participant : ObservableObject
{
    private bool _isMuted;

    public required string Name { get; init; }

    // ring + initials color in the member rail. no facecam in this design, so this is
    // the only per-person visual there is
    public IBrush AvatarBrush { get; init; } = Brushes.Gray;

    public bool IsStreamer { get; init; }

    // true for whoever is running this app
    public bool IsSelf { get; init; }

    // "Viewer 2" -> "V2"
    public string Initials
    {
        get
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "?",
                1 => parts[0][..1].ToUpperInvariant(),
                _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant(),
            };
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => SetProperty(ref _isMuted, value);
    }
}
