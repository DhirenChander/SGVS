using System.Collections.ObjectModel;
using SGVS.Frontend.Models;

namespace SGVS.Frontend.Services;

// what to share when starting a stream
public enum ShareSource
{
    Screen,
    App,
}

// the call as the UI sees it. FakeMeetingService stands in until the networking module is wired up.
// a real one should change Members and the participants' properties on the UI thread
public interface IMeetingService
{
    // join code people type on the Start screen
    string Code { get; }

    // whoever is running this app. also one of Members
    Participant Self { get; }

    // everyone in the call, in gallery order. grows and shrinks as people join and leave
    ReadOnlyObservableCollection<Participant> Members { get; }

    // starts sharing; Self.IsStreamer turns true once it's live
    Task StartStreamAsync(ShareSource source);

    Task StopStreamAsync();

    Task LeaveAsync();
}
