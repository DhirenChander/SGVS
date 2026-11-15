using System.Diagnostics;
using System.Text;

namespace SGVS.Frontend.Services.Networking;

// runs sgvs-networking as a child process in ipc mode and speaks the line protocol with it
public sealed class NetworkingProcess : INetworkingProcess
{
    private readonly Process _process;
    private readonly object _writeLock = new();
    private bool _disposed;

    public event Action<NetworkingEvent>? EventReceived;
    public event Action? Exited;

    // where the build drops the executable, next to the app (see SGVS.Frontend.csproj). null if it isn't there
    public static string? FindExecutable()
    {
        var name = OperatingSystem.IsWindows() ? "sgvs-networking.exe" : "sgvs-networking";
        var path = Path.Combine(AppContext.BaseDirectory, "backend", name);
        return File.Exists(path) ? path : null;
    }

    public NetworkingProcess(string executablePath)
    {
        var info = new ProcessStartInfo(executablePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false), // no BOM, it would corrupt the first command
            StandardOutputEncoding = Encoding.UTF8,
        };
        info.ArgumentList.Add("ipc");

        _process = new Process { StartInfo = info, EnableRaisingEvents = true };
        _process.Start();

        // logs from the networking module, nothing parses these
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) Trace.WriteLine("[networking] " + e.Data);
        };
        _process.BeginErrorReadLine();

        _ = Task.Run(ReadEventsAsync);
    }

    public void Send(NetworkingCommand command)
    {
        lock (_writeLock)
        {
            if (_disposed) return;
            try
            {
                _process.StandardInput.WriteLine(NetworkingProtocol.Serialize(command));
                _process.StandardInput.Flush();
            }
            catch (IOException)
            {
                // the process is gone, ReadEventsAsync reports that through Exited
            }
        }
    }

    private async Task ReadEventsAsync()
    {
        try
        {
            string? line;
            while ((line = await _process.StandardOutput.ReadLineAsync()) is not null)
            {
                if (NetworkingProtocol.TryParseEvent(line, out var evt))
                    EventReceived?.Invoke(evt);
                else
                    Trace.WriteLine("[networking] ignored output: " + line);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // stream closed under us, same as end of output
        }

        if (!_disposed) Exited?.Invoke();
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        try
        {
            // closing stdin is the polite way, the process notices and shuts down
            _process.StandardInput.Close();
            if (!_process.WaitForExit(2000)) _process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException)
        {
            // already gone
        }
        finally
        {
            _process.Dispose();
        }
    }
}
