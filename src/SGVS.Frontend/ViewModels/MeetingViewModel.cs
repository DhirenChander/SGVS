using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SGVS.Frontend.Models;
using SGVS.Frontend.Services;

namespace SGVS.Frontend.ViewModels;

// in a call: everyone's tile in a gallery, start/stop your own stream, watch someone else's. shown by MeetingView
public partial class MeetingViewModel : ViewModelBase
{
    // how long the check mark stays after copying the code
    private static readonly TimeSpan CopiedFeedback = TimeSpan.FromSeconds(1.5);

    private readonly IMeetingService _meeting;
    private readonly IClipboardService _clipboard;
    private readonly Action<ViewModelBase> _navigate;
    private CancellationTokenSource? _copiedReset;

    // for the XAML previewer only
    public MeetingViewModel() : this(new FakeMeetingService(), new ClipboardService(), _ => { })
    {
    }

    // navigate swaps the page MainWindow shows (see MainViewModel)
    public MeetingViewModel(IMeetingService meeting, IClipboardService clipboard, Action<ViewModelBase> navigate)
    {
        _meeting = meeting;
        _clipboard = clipboard;
        _navigate = navigate;

        // ReadOnlyObservableCollection only exposes the event through the interface
        ((INotifyCollectionChanged)Members).CollectionChanged += (_, _) => OnPropertyChanged(nameof(GalleryColumns));
        _meeting.Self.PropertyChanged += OnSelfChanged;
    }

    public ReadOnlyObservableCollection<Participant> Members => _meeting.Members;

    public string MeetingCode => _meeting.Code;

    // 1 person fills the screen, 2-4 is 2x2 like the mockup, then 3 and 4 across
    public int GalleryColumns => Members.Count switch
    {
        <= 1 => 1,
        <= 4 => 2,
        <= 9 => 3,
        _ => 4,
    };

    // follows the service, so a stream that drops on its own also flips the button back
    public bool IsStreaming => _meeting.Self.IsStreamer;

    // the glass Screen / App menu above Start stream
    [ObservableProperty]
    public partial bool IsShareMenuOpen { get; set; }

    // key icon swapped for the code
    [ObservableProperty]
    public partial bool IsCodeRevealed { get; set; }

    // copy icon swapped for a check, briefly
    [ObservableProperty]
    public partial bool IsCodeCopied { get; set; }

    [RelayCommand]
    private void ToggleShareMenu() => IsShareMenuOpen = !IsShareMenuOpen;

    public void CloseShareMenu() => IsShareMenuOpen = false;

    [RelayCommand]
    private async Task StartStream(ShareSource source)
    {
        IsShareMenuOpen = false;
        await _meeting.StartStreamAsync(source);
    }

    [RelayCommand]
    private Task StopStream() => _meeting.StopStreamAsync();

    // the Watching screen doesn't take a stream yet, so it opens with its own placeholder
    [RelayCommand]
    private void Watch(Participant member) => _navigate(new WatchViewModel());

    [RelayCommand]
    private async Task Leave()
    {
        await _meeting.LeaveAsync();
        _navigate(new StartViewModel());
    }

    // settings screen doesn't exist yet, the gear does nothing for now
    [RelayCommand]
    private void OpenSettings()
    {
    }

    [RelayCommand]
    private void RevealCode() => IsCodeRevealed = true;

    public void HideCode()
    {
        _copiedReset?.Cancel();
        IsCodeCopied = false;
        IsCodeRevealed = false;
    }

    // concurrent so clicking again during the check mark restarts it instead of being ignored
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task CopyCode()
    {
        if (!await _clipboard.TrySetTextAsync(MeetingCode)) return;

        _copiedReset?.Cancel();
        var reset = _copiedReset = new CancellationTokenSource();
        IsCodeCopied = true;

        try { await Task.Delay(CopiedFeedback, reset.Token); }
        catch (OperationCanceledException) { return; }

        IsCodeCopied = false;
    }

    private void OnSelfChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Participant.IsStreamer))
            OnPropertyChanged(nameof(IsStreaming));
    }
}
