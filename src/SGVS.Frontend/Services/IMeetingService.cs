using System.Collections.ObjectModel;
using System.ComponentModel;
using SGVS.Frontend.Models;

namespace SGVS.Frontend.Services;

// what to share when starting a stream
public enum ShareSource
{
    Screen,
    App,
}

// where the call is at. a call is you plus one other person for now
public enum CallState
{
    // nothing started yet
    Idle,

    // you are hosting: the connection string is ready and waiting for the guest's reply to be pasted in
    WaitingForReply,

    // you joined: your reply is ready and waiting for the host to paste it in
    WaitingForHost,

    Connected,

    // over: you left, the other person left, or the connection failed (see Error)
    Ended,
}

// the call as the UI sees it. FakeMeetingService is a pretend call, NetworkMeetingService talks to the
// networking module. changes Members and the participants' properties on the UI thread
// Dispose ends the call on this side: stops your stream and anyone else's on screen
public interface IMeetingService : INotifyPropertyChanged, IDisposable
{
    // the connection string to hand to the other person: your offer while hosting, your reply once you joined.
    // empty until the call is set up
    string Code { get; }

    CallState State { get; }

    // true if you started this call, false if you joined one. only the host has a code to share around
    bool IsHost { get; }

    // why the last thing failed, null if nothing has. shown on screen
    string? Error { get; }

    // whoever is running this app. also one of Members
    Participant Self { get; }

    // everyone in the call, in gallery order. grows and shrinks as people join and leave
    ReadOnlyObservableCollection<Participant> Members { get; }

    // start a call. false if it didn't work, Error says why
    bool Host();

    // join with the host's connection string. false if it didn't work, Error says why.
    // on success Code is the reply to send back to the host
    bool Join(string hostCode);

    // hosting: take the guest's reply. false if it didn't work, Error says why
    bool AcceptReply(string replyCode);

    // starts sharing; Self.IsStreamer turns true once it's live
    void StartStream(ShareSource source);

    void StopStream();

    void OnParticipantJoined(string name);

    // someone else's encoded stream started arriving. shows it on their tile and the Watch page. UI thread
    void AttachRemoteVideo(SGVS.Frontend.Services.Video.IEncodedVideoSource source);

    // Task LeaveAsync();
}
