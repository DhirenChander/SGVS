using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Text;


//using System.Reflection;
//using Avalonia.Platform.Surfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using SGVS.Frontend.Models;
using SGVS.Frontend.Services.Networking;

using SharedMemLibrary;

namespace SGVS.Frontend.Services;

// a real call: drives the networking module over its line protocol and the shared memory config.
//
// joining works with copy/paste connection strings since there is no server to meet at:
//   host:  HostAsync -> Code is the offer, hand it to the guest -> AcceptReplyAsync with what the guest sends back
//   guest: JoinAsync(offer) -> Code is the reply, hand it to the host
// networking has one peer connection, so a call is the host plus one guest.
public sealed class NetworkMeetingService : ObservableObject, IMeetingService
{
    // how long to wait for the networking module to answer a host/join before giving up
    private static readonly TimeSpan DefaultSetupTimeout = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _setupTimeout;

    private readonly Func<INetworkingProcess> _startProcess;
    private readonly Action<Action> _post;
    private readonly ObservableCollection<Participant> _members = [];

    private INetworkingProcess? _process;
    private IFrameBuffer _sharedMemory;

    // the host/join/accept the networking module still owes an answer to. completes true on success
    private TaskCompletionSource<bool>? _pending;
    private string _pendingEvent = "";

    // startProcess: launches the networking module. openSharedMemory: opens the shared frame buffer, called
    // the first time you stream. post: runs something on the UI thread (tests pass a direct call).
    // setupTimeout: how long host/join may take, 30 seconds if left out
    public NetworkMeetingService(
        string selfName,
        Func<INetworkingProcess> startProcess,
        Action<Action> post,
        TimeSpan? setupTimeout = null)
    {
        Console.WriteLine("Creating meeting service...");
        _setupTimeout = setupTimeout ?? DefaultSetupTimeout;
        _startProcess = startProcess;
        _post = post;
        Console.Write("Initializing shared memory...");

        _sharedMemory = new GCFrameBuffer();

        Console.WriteLine("Done.");

        Self = new Participant { Name = selfName, IsSelf = true, Tone = AvatarTone.Teal };
        _members.Add(Self);
        Members = new ReadOnlyObservableCollection<Participant>(_members);
    }

    public string Code { get; private set => SetProperty(ref field, value); } = "";

    public CallState State { get; private set => SetProperty(ref field, value); } = CallState.Idle;

    public bool IsHost { get; private set => SetProperty(ref field, value); }

    public string? Error { get; private set => SetProperty(ref field, value); }

    public Participant Self { get; }

    public ReadOnlyObservableCollection<Participant> Members { get; }

    public bool Host() =>
        Setup("Could not start the meeting", isHost: true, hostCode: "");

    public bool Join(string hostCode) =>

         Setup("Could not join the meeting", isHost: false, hostCode: hostCode.Trim());

    public bool AcceptReply(string replyCode)
    {
        return TryPush(
            new(System.Text.Encoding.UTF8.GetBytes(replyCode), SharedMemLibrary.Module.Application, SharedMemLibrary.Module.Network, SharedMemLibrary.Operation.AnswerCode),
            "Could not send the reply");
    }

    // pushes a message to the networking module. a missing/mismatched native library or a full
    // channel becomes an Error the UI can show instead of an unhandled exception
    private bool TryPush(Message message, string failurePrefix)
    {
        try
        {
            // PushMessage returns false when the ring is full, so treat that as a failure too
            if (_sharedMemory.PushMessage(message)) return true;
            Error = $"{failurePrefix}: the shared memory channel is full or unavailable.";
        }
        // these are what we get if the native dll is missing, the wrong bitness, or out of date with lib.cs
        catch (Exception e) when (e is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Error = $"{failurePrefix}: {e.Message}";
        }

        return false;
    }

    public void StartStream(ShareSource source)
    {
        try
        {
            _sharedMemory.PushMessage(new(null, SharedMemLibrary.Module.Application, SharedMemLibrary.Module.Network, Operation.StartSharing));
        }
        catch (Exception e) when (e is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Error = "Could not open the shared memory: " + e.Message;
            return;
        }

        Self.IsStreamer = true;
    }

    public void StopStream()
    {
        Self.IsStreamer = false;
        Self.StreamPreview = null;

        try { _sharedMemory.PushMessage(new(null, SharedMemLibrary.Module.Application, SharedMemLibrary.Module.Network, SharedMemLibrary.Operation.StopSharing)); }
        catch (Exception e) when (e is InvalidOperationException) { Error = "Could not update the shared memory: " + e.Message; }
    }

    /* TODO: Set up events in shared memory library to allow for leaving meetings
    public async Task LeaveAsync()
    {
        FailPending();
        if (_process is not null)
        {
            _process.Send(NetworkingCommand.Leave());
            _process.EventReceived -= OnEvent;
            _process.Exited -= OnExited;
            _process.Dispose();
            _process = null;
        }

        _sharedMemory?.Dispose();
        _sharedMemory = null;

        Self.IsStreamer = false;
        for (var i = _members.Count - 1; i >= 0; i--)
            if (!_members[i].IsSelf) _members.RemoveAt(i);

        State = CallState.Ended;
        await Task.CompletedTask;
    }*/

    // starts the process if it isn't running
    // called when starting or joining a meeting
    private bool Setup(string failurePrefix, bool isHost, string hostCode)
    {
        if (State is not (CallState.Idle or CallState.Ended) || _pending is not null)
        {
            Error = "Already in a meeting.";
            return false;
        }

        Error = null;
        IsHost = isHost;

        /*try
        {
            _process = _startProcess(isHost);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            Error = $"{failurePrefix}: {e.Message}";
            return false;
        }*/

        //_process.EventReceived += OnEvent;
        //_process.Exited += OnExited;

        /*var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = pending;
        _pendingEvent = answerEvent;
        _process.Send(command);*/

        //var finished = await Task.WhenAny(pending.Task, Task.Delay(_setupTimeout));
        // the host just asks the networking module for a host code (no payload), while a joiner
        // sends the code they were given across as an answer code
        var message = isHost
            ? new Message(null, SharedMemLibrary.Module.Application, SharedMemLibrary.Module.Network, SharedMemLibrary.Operation.HostCode)
            : new Message(System.Text.Encoding.UTF8.GetBytes(hostCode), SharedMemLibrary.Module.Application, SharedMemLibrary.Module.Network, SharedMemLibrary.Operation.AnswerCode);

        // bail out here so the UI shows the error from TryPush instead of waiting on a message that never went out
        if (!TryPush(message, failurePrefix)) return false;

        /*if (finished != pending.Task)
        {
            Error = $"{failurePrefix}: the networking module did not respond.";
            FailPending();
            return false;
        }*/

        return true; //await pending.Task;
    }

    /*
    private void OnEvent(SharedMemLibrary.Operation evt) => _post(() => Handle(evt));

    private void OnExited() => _post(() =>
    {
        if (_process is null) return; // we shut it down ourselves
        Error = "The networking module stopped unexpectedly.";
        State = CallState.Ended;
        //FailPending();
    });

    private void Handle(SharedMemLibrary.Operation op)
    {
        switch (op)
        {
            case SharedMemLibrary.Operation.StartSharing:
            case SharedMemLibrary.Operation.StopSharing:
            case SharedMemLibrary.Operation.HostCode:
            case SharedMemLibrary.Operation.AnswerCode:
            default:
                Console.WriteLine("Handle for operation not implemented");
        }
        /*
        switch (evt.Event)
        {
            case "offer" when evt.Code is not null:
                Code = evt.Code;
                State = CallState.WaitingForReply;
                CompletePending("offer", true);
                break;

            case "answer" when evt.Code is not null:
                Code = evt.Code;
                State = CallState.WaitingForHost;
                CompletePending("answer", true);
                break;

            case "state":
                OnConnectionState(evt.State);
                break;

            case "peer" when !string.IsNullOrWhiteSpace(evt.Name):
                if (_members.All(m => m.IsSelf || m.Name != evt.Name))
                    _members.Add(new Participant { Name = evt.Name, Tone = AvatarTone.Amber });
                break;

            case "peer_stream":
                foreach (var peer in _members.Where(m => !m.IsSelf))
                {
                    peer.IsStreamer = evt.On == true;
                    // there is no video to show yet, the tile just switches to its streaming look
                    peer.StreamPreview = null;
                }
                break;

            case "peer_left":
                for (var i = _members.Count - 1; i >= 0; i--)
                    if (!_members[i].IsSelf) _members.RemoveAt(i);
                // one peer connection per call, once it's gone this call is over
                State = CallState.Ended;
                break;

            case "error":
                Error = evt.Message ?? "Something went wrong.";
                FailPending();
                break;
        }
    }*/

    /*
    private void OnConnectionState(string? state)
    {
        switch (state)
        {
            case "connected":
                State = CallState.Connected;
                // they may have joined after we started sharing, tell them
                if (Self.IsStreamer) _process?.Send(NetworkingCommand.Stream(true));
                break;

            case "failed":
                Error = "The connection failed.";
                State = CallState.Ended;
                break;
        }
    }*/

    /*
    private void CompletePending(string forEvent, bool success)
    {
        if (_pending is null || _pendingEvent != forEvent) return;
        var pending = _pending;
        _pending = null;
        pending.TrySetResult(success);
    }

    private void FailPending()
    {
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(false);
    }*/
}