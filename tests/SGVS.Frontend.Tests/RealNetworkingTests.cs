using System.Diagnostics;
using SGVS.Frontend.Services;
using SGVS.Frontend.Services.Networking;
using SGVS.Frontend.Services.SharedMemory;
using Xunit;

namespace SGVS.Frontend.Tests;

// builds the real Go networking module and drives two copies of it through NetworkMeetingService, the same
// way two running apps would. everything runs on this machine over loopback
public sealed class GoModuleFixture : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sgvs-net-" + Guid.NewGuid().ToString("N"));

    // null if Go isn't available, tests skip themselves then
    public string? Executable { get; }
    public string? BuildProblem { get; }

    public GoModuleFixture()
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            BuildProblem = "could not find the repository root (SGVS.slnx)";
            return;
        }

        Directory.CreateDirectory(_dir);
        var exe = Path.Combine(_dir, OperatingSystem.IsWindows() ? "sgvs-networking.exe" : "sgvs-networking");

        try
        {
            var info = new ProcessStartInfo("go")
            {
                WorkingDirectory = Path.Combine(root, "src", "networking"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.ArgumentList.Add("build");
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add(exe);
            info.ArgumentList.Add(".");

            using var go = Process.Start(info)!;
            var errors = go.StandardError.ReadToEnd();
            go.WaitForExit();

            if (go.ExitCode == 0) Executable = exe;
            else BuildProblem = "go build failed: " + errors;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            BuildProblem = "Go is not installed";
        }
    }

    private static string? FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SGVS.slnx"))) return dir.FullName;
        return null;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* still locked, the OS temp cleanup gets it */ }
    }
}

public class RealNetworkingTests(GoModuleFixture go) : IClassFixture<GoModuleFixture>
{
    private NetworkMeetingService NewService(string name)
    {
        Assert.SkipWhen(go.Executable is null, go.BuildProblem ?? "networking module not built");
        var exe = go.Executable!;
        return new NetworkMeetingService(name, () => new NetworkingProcess(exe), () => new NullSharedMemoryBridge(), action => action());
    }

    // the services update from background threads here, so poll instead of assuming it's instant
    private static async Task Eventually(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.Fail("timed out waiting for: " + what);
    }

    private static async Task<(NetworkMeetingService host, NetworkMeetingService guest)> Connect(
        Func<string, NetworkMeetingService> create)
    {
        var host = create("Hazel");
        var guest = create("Gus");

        Assert.True(await host.HostAsync(), host.Error);
        Assert.False(string.IsNullOrWhiteSpace(host.Code));

        Assert.True(await guest.JoinAsync(host.Code), guest.Error);
        Assert.False(string.IsNullOrWhiteSpace(guest.Code));

        Assert.True(await host.AcceptReplyAsync(guest.Code));
        await Eventually(() => host.State == CallState.Connected && guest.State == CallState.Connected, "both sides connected");
        return (host, guest);
    }

    [Fact]
    public async Task Host_and_guest_connect_and_see_each_other()
    {
        var (host, guest) = await Connect(NewService);
        try
        {
            await Eventually(() => host.Members.Count == 2 && guest.Members.Count == 2, "both galleries to have two people");

            Assert.Equal(["Hazel", "Gus"], host.Members.Select(m => m.Name));
            Assert.Equal(["Gus", "Hazel"], guest.Members.Select(m => m.Name));
            Assert.True(host.IsHost);
            Assert.False(guest.IsHost);
        }
        finally
        {
            await host.LeaveAsync();
            await guest.LeaveAsync();
        }
    }

    [Fact]
    public async Task Streaming_state_reaches_the_other_person_both_ways()
    {
        var (host, guest) = await Connect(NewService);
        try
        {
            await Eventually(() => host.Members.Count == 2 && guest.Members.Count == 2, "both galleries to have two people");

            await host.StartStreamAsync(ShareSource.Screen);
            await Eventually(() => guest.Members.Single(m => !m.IsSelf).IsStreamer, "guest to see the host streaming");

            await host.StopStreamAsync();
            await Eventually(() => !guest.Members.Single(m => !m.IsSelf).IsStreamer, "guest to see the stream stop");

            await guest.StartStreamAsync(ShareSource.App);
            await Eventually(() => host.Members.Single(m => !m.IsSelf).IsStreamer, "host to see the guest streaming");
        }
        finally
        {
            await host.LeaveAsync();
            await guest.LeaveAsync();
        }
    }

    [Fact]
    public async Task Leaving_ends_the_call_for_the_other_person()
    {
        var (host, guest) = await Connect(NewService);
        try
        {
            await Eventually(() => host.Members.Count == 2, "host to see the guest");

            await guest.LeaveAsync();

            await Eventually(() => host.State == CallState.Ended, "host to notice the guest left");
            Assert.Single(host.Members);
        }
        finally
        {
            await host.LeaveAsync();
        }
    }

    [Fact]
    public async Task A_garbage_connection_string_is_rejected_by_the_real_module()
    {
        var guest = NewService("Gus");
        try
        {
            Assert.False(await guest.JoinAsync("this is not a connection string"));

            Assert.NotNull(guest.Error);
            Assert.Equal(CallState.Idle, guest.State);
        }
        finally
        {
            await guest.LeaveAsync();
        }
    }

    [Fact]
    public async Task A_garbage_reply_is_rejected_and_a_good_one_still_works_afterwards()
    {
        var host = NewService("Hazel");
        var guest = NewService("Gus");
        try
        {
            await host.HostAsync();
            await guest.JoinAsync(host.Code);

            await host.AcceptReplyAsync("garbage");
            await Eventually(() => host.Error is not null, "an error about the bad reply");
            Assert.Equal(CallState.WaitingForReply, host.State);

            await host.AcceptReplyAsync(guest.Code);
            await Eventually(() => host.State == CallState.Connected, "the real reply to connect");
            Assert.Null(host.Error);
        }
        finally
        {
            await host.LeaveAsync();
            await guest.LeaveAsync();
        }
    }

    [Fact]
    public async Task The_module_process_exits_when_the_app_leaves()
    {
        Assert.SkipWhen(go.Executable is null, go.BuildProblem ?? "networking module not built");
        var before = Process.GetProcessesByName("sgvs-networking").Select(p => p.Id).ToHashSet();
        var host = NewService("Hazel");
        await host.HostAsync();
        var started = Process.GetProcessesByName("sgvs-networking").Where(p => !before.Contains(p.Id)).ToList();
        Assert.NotEmpty(started);

        await host.LeaveAsync();

        foreach (var process in started)
        {
            Assert.True(process.WaitForExit(5000), "the networking process should have shut down");
        }
    }
}
