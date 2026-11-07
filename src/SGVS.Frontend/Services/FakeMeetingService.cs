using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using SGVS.Frontend.Models;

namespace SGVS.Frontend.Services;

// pretend call with the people from the Figma mockup. no networking, everything happens instantly
public sealed class FakeMeetingService : IMeetingService
{
    // stand-in stream frame, StreamPreview/landscape.jpg next to the exe. null if it's missing
    private readonly Bitmap? _preview = LoadImage("StreamPreview", "landscape.jpg");

    public FakeMeetingService()
    {
        // display names aren't decided yet, "Alex" is only here so your avatar has an initial
        Self = new Participant { Name = "Alex", IsSelf = true, Tone = AvatarTone.Teal };

        Members = new ReadOnlyObservableCollection<Participant>(
        [
            new Participant { Name = "Sam", Tone = AvatarTone.Amber, IsStreamer = true, StreamPreview = _preview },
            Self,
            new Participant { Name = "Priya", Tone = AvatarTone.Sand },
            new Participant { Name = "Jo", Tone = AvatarTone.Neutral },
        ]);
    }

    public string Code => "KX7-29Q";

    public Participant Self { get; }

    public ReadOnlyObservableCollection<Participant> Members { get; }

    // screen and app share the same placeholder until capture exists
    public Task StartStreamAsync(ShareSource source)
    {
        Self.StreamPreview = _preview;
        Self.IsStreamer = true;
        return Task.CompletedTask;
    }

    public Task StopStreamAsync()
    {
        Self.IsStreamer = false;
        Self.StreamPreview = null;
        return Task.CompletedTask;
    }

    public Task LeaveAsync() => Task.CompletedTask;

    // images live in folders next to the exe, null if missing or unreadable
    private static Bitmap? LoadImage(string folder, string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, folder, file);
        if (!File.Exists(path)) return null;

        try { return new Bitmap(path); }
        catch (Exception) { return null; }
    }
}
