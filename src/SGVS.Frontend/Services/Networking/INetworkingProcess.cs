namespace SGVS.Frontend.Services.Networking;

// the networking module as the frontend sees it: commands go in, events come out.
// NetworkingProcess is the real one, tests use a fake
public interface INetworkingProcess : IDisposable
{
    // raised on a background thread
    event Action<NetworkingEvent>? EventReceived;

    // raised once if the process ends without being disposed (crash, killed from outside)
    event Action? Exited;

    void Send(NetworkingCommand command);
}
