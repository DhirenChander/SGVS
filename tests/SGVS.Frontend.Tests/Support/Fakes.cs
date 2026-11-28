using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SGVS.Frontend.Models;
using SGVS.Frontend.Services;
using SGVS.Frontend.Services.Networking;
using SGVS.Frontend.Services.Video;

namespace SGVS.Frontend.Tests.Support;

// stands in for the networking module process. just remembers what it was sent
public sealed class FakeNetworkingProcess : INetworkingProcess
{
    public List<NetworkingCommand> Sent { get; } = [];

    public event Action<NetworkingEvent>? EventReceived { add { } remove { } }
    public event Action? Exited { add { } remove { } }

    public void Send(NetworkingCommand command) => Sent.Add(command);

    public void Dispose()
    {
    }
}

// a call the tests drive by hand, for the view models and pages. host/join/accept do what the real
// service is meant to do once networking answers, and the tests change State, Error and Members directly
// to play out the rest
public sealed class ScriptedMeetingService : ObservableObject, IMeetingService
{
    private readonly ObservableCollection<Participant> _members = [];

    public ScriptedMeetingService()
    {
        Self = new Participant { Name = "Hazel", IsSelf = true, Tone = AvatarTone.Teal };
        _members.Add(Self);
        Members = new ReadOnlyObservableCollection<Participant>(_members);
    }

    public string Code { get; private set => SetProperty(ref field, value); } = "";

    public CallState State { get; set => SetProperty(ref field, value); } = CallState.Idle;

    public bool IsHost { get; private set => SetProperty(ref field, value); }

    public string? Error { get; set => SetProperty(ref field, value); }

    public Participant Self { get; }

    public ReadOnlyObservableCollection<Participant> Members { get; }

    // false makes host/join/accept fail with FailWith as the error
    public bool Succeeds { get; set; } = true;

    public string FailWith { get; set; } = "something went wrong";

    // what was called, in order
    public List<string> Calls { get; } = [];

    public bool Host()
    {
        Calls.Add("host");
        if (!Succeeds) return Fail();

        IsHost = true;
        Code = "OFFER-CODE";
        State = CallState.WaitingForReply;
        return true;
    }

    public bool Join(string hostCode)
    {
        Calls.Add("join:" + hostCode);
        if (!Succeeds) return Fail();

        Code = "ANSWER-CODE";
        State = CallState.WaitingForHost;
        return true;
    }

    public bool AcceptReply(string replyCode)
    {
        Calls.Add("accept:" + replyCode);
        return Succeeds || Fail();
    }

    public void StartStream(ShareSource source)
    {
        Calls.Add("start:" + source);
        Self.IsStreamer = true;
    }

    public void StopStream()
    {
        Calls.Add("stop");
        Self.IsStreamer = false;
        Self.StreamPreview = null;
    }

    public void OnParticipantJoined(string name) => AddPeer(name);

    public void AttachRemoteVideo(IEncodedVideoSource source) => Calls.Add("remote-video");

    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;

    public void AddPeer(string name) => _members.Add(new Participant { Name = name, Tone = AvatarTone.Amber });

    private bool Fail()
    {
        Error = FailWith;
        return false;
    }
}
