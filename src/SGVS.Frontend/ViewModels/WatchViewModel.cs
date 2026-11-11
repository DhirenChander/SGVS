using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using SGVS.Frontend.Models;
using SGVS.Frontend.Services;

namespace SGVS.Frontend.ViewModels;

public class WatchViewModel : ViewModelBase
{
    // for the XAML previewer only, watches whoever is streaming in the fake call
    public WatchViewModel() : this(Watched(new FakeMeetingService()), new FakeMeetingService(), () => { }, () => { })
    {
    }

    // member: whose stream this is. meeting: the same call the Meeting page shows, so both pages
    // list the same people. stopWatching: what the Stop watching button does (back to Meeting). goToStart: what Leave does
    public WatchViewModel(Participant member, IMeetingService meeting, Action stopWatching, Action goToStart)
    {
        Participants = meeting.Members;
        StreamFrame = member.StreamPreview;
        JoinCode = new JoinCodeViewModel(meeting.Code);

        // control bar + gear. Start stream is null since there's nothing to start from here
        // yet, and Leave goes back to Start
        Overlay = new OverlayViewModel(meeting.Self, showViewerControls: true, startStream: null, stopStream: null,
            leave: async () =>
            {
                await meeting.LeaveAsync();
                goToStart();
            },
            stopWatching: stopWatching);
    }

    // first person sharing, only used to feed the previewer constructor
    private static Participant Watched(IMeetingService meeting) => meeting.Members.First(m => m.IsStreamer);

    // bar, gear and dialogs drawn by StreamOverlay
    public OverlayViewModel Overlay { get; }

    // the streamer's latest frame, the same one their Meeting tile shows
    public Bitmap? StreamFrame { get; }

    public ReadOnlyObservableCollection<Participant> Participants { get; }

    // the key icon / code rail at the bottom-right
    public JoinCodeViewModel JoinCode { get; }
}
