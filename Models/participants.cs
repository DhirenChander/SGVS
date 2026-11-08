using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SVGS.Frontend.Models;

public class Participant : ObservableObject
{
    private bool _isMuted;

    public required string Name { get; init; }

    // flat avatar color, one of apple's system colors
    public IBrush AvatarBrush { get; init; } = Brushes.Gray;

    // stand-in for their camera feed, null = camera off
    public Bitmap? VideoFrame { get; set; }

    public bool IsVideoOn => VideoFrame is not null;

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