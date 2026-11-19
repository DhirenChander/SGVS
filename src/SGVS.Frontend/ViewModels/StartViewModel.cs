using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SGVS.Frontend.Services;

namespace SGVS.Frontend.ViewModels;

// home screen: start a meeting or join one with the host's connection string. shown by StartView.
// hosting goes straight to the meeting page. joining first shows the reply to send back to the host,
// and moves on once the host has pasted it in and the connection is up
public partial class StartViewModel : ViewModelBase
{
    private readonly IMeetingService _meeting;
    private readonly Action _openMeeting;
    private readonly Action _startOver;
    private bool _opened;

    // for the XAML previewer only
    public StartViewModel() : this(new FakeMeetingService(), () => { }, () => { })
    {
    }

    // openMeeting: go to the meeting page with this service. startOver: throw this attempt away, back to a fresh Start page
    public StartViewModel(IMeetingService meeting, Action openMeeting, Action startOver)
    {
        _meeting = meeting;
        _openMeeting = openMeeting;
        _startOver = startOver;
        _meeting.PropertyChanged += OnMeetingChanged;
    }

    // what the user pasted into the box
    [ObservableProperty]
    public partial string JoinText { get; set; } = "";

    // host/join in progress, buttons wait their turn
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public string? ErrorMessage => _meeting.Error;

    public bool HasError => _meeting.Error is not null;

    // joined, now the host needs our reply
    public bool IsAwaitingHost => _meeting.State == CallState.WaitingForHost;

    // what to send to the host
    public string ReplyCode => _meeting.Code;

    [RelayCommand]
    private void Host()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (_meeting.Host()) OpenMeetingOnce();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Join()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            _meeting.Join(JoinText);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // gives up on a join that is waiting for the host
    [RelayCommand]
    private void Cancel() => _startOver();

    private void OnMeetingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IMeetingService.Error):
                OnPropertyChanged(nameof(ErrorMessage));
                OnPropertyChanged(nameof(HasError));
                break;

            case nameof(IMeetingService.State):
                OnPropertyChanged(nameof(IsAwaitingHost));
                if (_meeting.State == CallState.Connected) OpenMeetingOnce();
                break;

            case nameof(IMeetingService.Code):
                OnPropertyChanged(nameof(ReplyCode));
                break;
        }
    }

    private void OpenMeetingOnce()
    {
        if (_opened) return;
        _opened = true;
        _meeting.PropertyChanged -= OnMeetingChanged;
        _openMeeting();
    }
}
