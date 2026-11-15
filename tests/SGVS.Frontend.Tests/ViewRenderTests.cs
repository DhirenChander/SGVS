using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SGVS.Frontend;
using SGVS.Frontend.Services;
using SGVS.Frontend.Tests.Support;
using SGVS.Frontend.ViewModels;
using SGVS.Frontend.Views;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(SGVS.Frontend.Tests.TestApp))]

namespace SGVS.Frontend.Tests;

public class TestApp
{
    // real fonts and rendering, otherwise text layout throws on the first TextBlock
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().WithInterFont().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

// puts the real windows on a headless screen to catch XAML and binding mistakes that only show up at runtime
public class ViewRenderTests
{
    private readonly FakeNetworkingProcess _process = new();

    private NetworkMeetingService NewService() =>
        new("Hazel", () => _process, () => new FakeSharedMemory(), action => action());

    private static MainWindow Show(MainViewModel vm)
    {
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static IEnumerable<string> Texts(Visual root) =>
        root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "");

    private static void Click(Window window, string content)
    {
        var button = window.GetVisualDescendants().OfType<Button>()
            .First(b => b.IsEffectivelyVisible && b.Content as string == content);
        button.Command!.Execute(button.CommandParameter);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Start_screen_shows_join_box_and_new_meeting()
    {
        var window = Show(new MainViewModel(NewService));

        Assert.Contains("New meeting", window.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string));
        Assert.NotEmpty(window.GetVisualDescendants().OfType<TextBox>());
    }

    [AvaloniaFact]
    public void Start_screen_shows_the_error_when_hosting_fails()
    {
        _process.Script = _ => [new() { Event = "error", Message = "could not host: no network" }];
        var window = Show(new MainViewModel(NewService));

        Click(window, "New meeting");

        Assert.Contains("could not host: no network", Texts(window));
    }

    [AvaloniaFact]
    public void Joining_shows_the_reply_to_copy_and_cancel()
    {
        var window = Show(new MainViewModel(NewService));
        ((StartViewModel)((MainViewModel)window.DataContext!).CurrentView).JoinText = "host-code";

        Click(window, "→");

        Assert.Contains("Copy reply", window.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string));
        Assert.Contains(window.GetVisualDescendants().OfType<TextBox>(), t => t.IsReadOnly && t.Text == "ANSWER-CODE");
        Assert.DoesNotContain("New meeting", window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string));
    }

    [AvaloniaFact]
    public void Hosting_shows_the_meeting_with_the_reply_box()
    {
        var window = Show(new MainViewModel(NewService));

        Click(window, "New meeting");

        var meeting = window.GetVisualDescendants().OfType<MeetingView>().Single();
        Assert.Contains("Connect", meeting.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string));
        Assert.Contains(meeting.GetVisualDescendants().OfType<TextBox>(), t => t.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Meeting_reply_box_disappears_once_the_guest_connects()
    {
        var window = Show(new MainViewModel(NewService));
        Click(window, "New meeting");

        _process.Emit("state", state: "connected");
        _process.Emit("peer", peer: "Gus");
        Dispatcher.UIThread.RunJobs();

        var meeting = window.GetVisualDescendants().OfType<MeetingView>().Single();
        Assert.DoesNotContain("Connect", meeting.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string));
        Assert.Contains("Gus", Texts(meeting)); // their tile
    }

    [AvaloniaFact]
    public void Meeting_reports_errors_and_a_finished_call()
    {
        var window = Show(new MainViewModel(NewService));
        Click(window, "New meeting");

        _process.Emit("error", message: "could not accept reply: bad");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("could not accept reply: bad", Texts(window));

        _process.Emit("state", state: "connected");
        _process.Emit("peer", peer: "Gus");
        _process.Emit("peer_left");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Texts(window), t => t.Contains("The call has ended"));
    }

    [AvaloniaFact]
    public void The_fake_call_renders_meeting_and_watch_pages()
    {
        var vm = new MainViewModel(() => new FakeMeetingService());
        var window = Show(vm);

        Click(window, "New meeting");
        var meeting = Assert.IsType<MeetingViewModel>(vm.CurrentView);
        Assert.NotEmpty(window.GetVisualDescendants().OfType<MeetingView>());

        meeting.WatchCommand.Execute(meeting.Members.First(m => m.IsStreamer));
        Dispatcher.UIThread.RunJobs();
        Assert.NotEmpty(window.GetVisualDescendants().OfType<WatchView>());
    }
}
