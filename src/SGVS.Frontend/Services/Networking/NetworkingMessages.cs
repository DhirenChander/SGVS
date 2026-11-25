using System.Text.Json;
using System.Text.Json.Serialization;

namespace SGVS.Frontend.Services.Networking;

// the lines exchanged with the Go networking process (`sgvs-networking ipc`), one JSON object per line.
// the other end of this is src/networking/ipc.go, keep the two in step.

// frontend -> networking. which fields matter depends on Cmd:
//   host{name}  join{name,code}  accept{code}  stream{on}  leave
public sealed class NetworkingCommand
{
    [JsonPropertyName("cmd")]
    public required string Cmd { get; init; }

    [JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    [JsonPropertyName("code"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; init; }

    // always written for stream so that "off" isn't dropped
    [JsonPropertyName("on"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? On { get; init; }

    public static NetworkingCommand Host(string name) => new() { Cmd = "host", Name = name };
    public static NetworkingCommand Join(string name, string code) => new() { Cmd = "join", Name = name, Code = code };
    public static NetworkingCommand Accept(string code) => new() { Cmd = "accept", Code = code };
    public static NetworkingCommand Stream(bool on) => new() { Cmd = "stream", On = on };
    public static NetworkingCommand Leave() => new() { Cmd = "leave" };
}

// networking -> frontend:
//   offer{code}  answer{code}  state{state}  peer{name}  peer_stream{on}  peer_left  left  error{message}
public sealed class NetworkingEvent
{
    [JsonPropertyName("event")]
    public string Event { get; init; } = "";

    [JsonPropertyName("code")]
    public string? Code { get; init; }

    // WebRTC peer connection state: new, connecting, connected, disconnected, failed, closed
    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("on")]
    public bool? On { get; init; }
}

public static class NetworkingProtocol
{
    public static string Serialize(NetworkingCommand command) => JsonSerializer.Serialize(command);

    // false for blank lines, malformed JSON and objects with no event name, none of which should
    // take the app down
    public static bool TryParseEvent(string line, out NetworkingEvent evt)
    {
        evt = new NetworkingEvent();
        if (string.IsNullOrWhiteSpace(line)) return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<NetworkingEvent>(line);
            if (parsed is null || string.IsNullOrEmpty(parsed.Event)) return false;
            evt = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
