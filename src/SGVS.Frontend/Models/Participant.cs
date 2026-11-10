using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SGVS.Frontend.Models;

public class Participant : ObservableObject
{
    private bool _isMuted;
    private bool _isStreamer;
    private Bitmap? _streamPreview;

    public required string Name { get; init; }

    // ring + initials color in the member rail. no facecam in this design, so this is
    // the only per-person visual there is
    public IBrush AvatarBrush { get; init; } = Brushes.Gray;

    // avatar color on the Meeting screen, mapped to a token in Styles/Resources.axaml
    public AvatarTone Tone { get; init; } = AvatarTone.Neutral;

    // sharing their screen right now. settable because people start and stop mid-call
    public bool IsStreamer
    {
        get => _isStreamer;
        set => SetProperty(ref _isStreamer, value);
    }

    // latest frame of their stream for the gallery tile, null while nothing has arrived
    public Bitmap? StreamPreview
    {
        get => _streamPreview;
        set => SetProperty(ref _streamPreview, value);
    }

    // true for whoever is running this app
    public bool IsSelf { get; init; }

    // what the gallery tile says: your own tile reads "You"
    public string TileLabel => IsSelf ? "You" : Name;

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
