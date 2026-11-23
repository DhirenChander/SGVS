using System.Text.Json;
using SGVS.Frontend.Services.Networking;
using Xunit;

namespace SGVS.Frontend.Tests;

public class NetworkingProtocolTests
{
    private static JsonElement Parse(NetworkingCommand command) =>
        JsonDocument.Parse(NetworkingProtocol.Serialize(command)).RootElement;

    [Fact]
    public void Host_command_has_cmd_and_name_only()
    {
        var json = Parse(NetworkingCommand.Host("Hazel"));

        Assert.Equal("host", json.GetProperty("cmd").GetString());
        Assert.Equal("Hazel", json.GetProperty("name").GetString());
        Assert.False(json.TryGetProperty("code", out _));
        Assert.False(json.TryGetProperty("on", out _));
    }

    [Fact]
    public void Join_and_accept_carry_the_code()
    {
        var join = Parse(NetworkingCommand.Join("Gus", "abc"));
        Assert.Equal("join", join.GetProperty("cmd").GetString());
        Assert.Equal("Gus", join.GetProperty("name").GetString());
        Assert.Equal("abc", join.GetProperty("code").GetString());

        var accept = Parse(NetworkingCommand.Accept("xyz"));
        Assert.Equal("accept", accept.GetProperty("cmd").GetString());
        Assert.Equal("xyz", accept.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Stream_command_always_includes_on(bool on)
    {
        // a missing "on" would be read as false by the Go side, so false must still be written explicitly
        var json = Parse(NetworkingCommand.Stream(on));
        Assert.Equal(on, json.GetProperty("on").GetBoolean());
    }

    [Fact]
    public void Serialized_command_is_a_single_line()
    {
        Assert.DoesNotContain('\n', NetworkingProtocol.Serialize(NetworkingCommand.Join("a\nb", "c\r\nd")));
    }

    [Fact]
    public void Parses_each_event_kind()
    {
        Assert.True(NetworkingProtocol.TryParseEvent("""{"event":"offer","code":"abc"}""", out var offer));
        Assert.Equal("offer", offer.Event);
        Assert.Equal("abc", offer.Code);

        Assert.True(NetworkingProtocol.TryParseEvent("""{"event":"state","state":"connected"}""", out var state));
        Assert.Equal("connected", state.State);

        Assert.True(NetworkingProtocol.TryParseEvent("""{"event":"peer","name":"Gus"}""", out var peer));
        Assert.Equal("Gus", peer.Name);

        Assert.True(NetworkingProtocol.TryParseEvent("""{"event":"peer_stream","on":false}""", out var stream));
        Assert.False(stream.On);

        Assert.True(NetworkingProtocol.TryParseEvent("""{"event":"error","message":"nope"}""", out var error));
        Assert.Equal("nope", error.Message);

        Assert.True(NetworkingProtocol.TryParseEvent("""{"event":"peer_left"}""", out var left));
        Assert.Null(left.Code);
    }

    [Fact]
    public void Ignores_fields_it_does_not_know()
    {
        Assert.True(NetworkingProtocol.TryParseEvent("""{"event":"state","state":"new","future":42}""", out var evt));
        Assert.Equal("new", evt.State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"event":""}""")]
    [InlineData("""[1,2,3]""")]
    public void Rejects_lines_that_are_not_events(string line)
    {
        Assert.False(NetworkingProtocol.TryParseEvent(line, out _));
    }
}
