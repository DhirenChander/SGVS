using SGVS.Frontend.Services.Networking;
using SGVS.Frontend.Services.SharedMemory;

namespace SGVS.Frontend.Tests.Support;

// stands in for the networking module. records what was sent, and replies the way the real one does
// (host -> offer, join -> answer) unless told otherwise
public sealed class FakeNetworkingProcess : INetworkingProcess
{
    public List<NetworkingCommand> Sent { get; } = [];
    public bool Disposed { get; private set; }

    // what to emit when a command arrives. null means the default replies
    public Func<NetworkingCommand, IEnumerable<NetworkingEvent>>? Script { get; set; }

    public event Action<NetworkingEvent>? EventReceived;
    public event Action? Exited;

    public void Send(NetworkingCommand command)
    {
        Sent.Add(command);
        foreach (var evt in (Script ?? DefaultReplies)(command))
            EventReceived?.Invoke(evt);
    }

    public void Emit(NetworkingEvent evt) => EventReceived?.Invoke(evt);

    public void Emit(string name, string? code = null, string? state = null, string? peer = null, string? message = null, bool? on = null) =>
        Emit(new NetworkingEvent { Event = name, Code = code, State = state, Name = peer, Message = message, On = on });

    public void Crash() => Exited?.Invoke();

    public void Dispose() => Disposed = true;

    private static IEnumerable<NetworkingEvent> DefaultReplies(NetworkingCommand command)
    {
        switch (command.Cmd)
        {
            case "host":
                yield return new NetworkingEvent { Event = "offer", Code = "OFFER-CODE" };
                break;
            case "join":
                yield return new NetworkingEvent { Event = "answer", Code = "ANSWER-CODE" };
                break;
        }
    }
}

public sealed class FakeSharedMemory : ISharedMemoryBridge
{
    public bool IsSharingVideo { get; private set; }
    public bool Disposed { get; private set; }

    // makes SetSharingVideo fail like the real one does when the native library isn't usable
    public bool Fail { get; set; }

    public void SetSharingVideo(bool sharing)
    {
        if (Fail) throw new InvalidOperationException("no shared memory here");
        IsSharingVideo = sharing;
    }

    public void Dispose() => Disposed = true;
}
