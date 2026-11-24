using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SGVS.Frontend.Models;
using SGVS.Frontend.Services;
using SGVS.Frontend.Tests.Support;
using SGVS.Frontend.ViewModels;
using SGVS.Frontend.Views;
using Xunit;

namespace SGVS.Frontend.Tests;

// real bitmaps and windows, so these run on the headless app from ViewRenderTests.cs
public class WatchStreamTests
{
    private static WriteableBitmap NewBitmap() =>
        new(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    [AvaloniaFact]
    public void Watch_page_follows_the_streamers_preview()
    {
        var member = new Participant { Name = "Pat", IsStreamer = true };
        var watch = new WatchViewModel(member, new ScriptedMeetingService(), () => { }, () => { });
        var changed = new List<string?>();
        watch.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var frame = NewBitmap();
        member.StreamPreview = frame;
        Assert.Same(frame, watch.StreamFrame);

        member.StreamPreview = null;
        Assert.Null(watch.StreamFrame);

        Assert.Equal(2, changed.Count(n => n == nameof(WatchViewModel.StreamFrame)));
    }

    [AvaloniaFact]
    public void Your_own_tile_shows_a_streaming_badge_and_no_watch_button()
    {
        var service = new ScriptedMeetingService();
        service.Self.IsStreamer = true;
        var main = new MainViewModel(() => service);
        var window = new MainWindow { DataContext = main };
        window.Show();
        ((StartViewModel)main.CurrentView).HostCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var watchButtons = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Watch Hazel");

        Assert.Empty(watchButtons);
        Assert.Single(window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Text == "Streaming"));
    }
}
