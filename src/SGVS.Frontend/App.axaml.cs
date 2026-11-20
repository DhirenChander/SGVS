using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SGVS.Frontend.ViewModels;
using SGVS.Frontend.Views;
using System.Threading.Channels;
using SharedMemLibrary;

namespace SGVS.Frontend;

public partial class App : Application
{
    private CancellationTokenSource? _appCts;
    private IFrameBuffer? _sharedMemory;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _sharedMemory = new GCFrameBuffer();
            var main = new MainViewModel(_sharedMemory);
            desktop.MainWindow = new MainWindow
            {
                DataContext = main,
            };

            Channel<GCFrame> audioChan = Channel.CreateBounded<GCFrame>(200); // Holds 2 seconds of audio

            _appCts = new CancellationTokenSource();
            var appToken = _appCts.Token;

            // 1. Start the app-lifetime background thread here
            StartBackgroundWorkers(audioChan.Writer, appToken);

            // the networking module is a child process, don't leave it running
            desktop.ShutdownRequested += (_, _) =>
            {
                _appCts?.Cancel();
                _appCts?.Dispose();
                main.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartBackgroundWorkers(ChannelWriter<GCFrame> audioWriter, CancellationToken appToken)
    {
        Task.Factory.StartNew(() => EncodingWatcher(audioWriter, appToken), cancellationToken: appToken);

        Task.Factory.StartNew(MessageWatcher, cancellationToken: appToken);
    }

    private void MessageWatcher()
    {

    }

    private void EncodingWatcher(ChannelWriter<GCFrame> audioWriter, CancellationToken appToken)
    {
        if (_sharedMemory is null) return;
        HashSet<(ulong, ulong, ulong, ulong)> running_encs = new();

        while (!appToken.IsCancellationRequested)
        {
            (Encoding[] encodings, bool success) = _sharedMemory.ReadEncodings(10);

            if (success)
            {
                Console.WriteLine("Received " + encodings.Length.ToString() + " Encodings");

                bool aligned = false;

                foreach (Encoding enc in encodings)
                {
                    // ids are 256 bits wide now, so a single ulong no longer fits. use the four limbs as the set key
                    var encKey = (enc.id[0], enc.id[1], enc.id[2], enc.id[3]);

                    if (running_encs.Contains(encKey))
                    {
                        continue;
                    }

                    // Must align before starting receiver thread
                    while (!aligned)
                    {
                        aligned = _sharedMemory.AlignRingsToConfig();
                    }

                    running_encs.Add(encKey);

                    if (enc.IsIncoming())
                    {
                        Task.Factory.StartNew(() => Receiver(enc, audioWriter, appToken), cancellationToken: appToken);
                    }
                }
            }
            else
            {
                Console.WriteLine("Failed to fetch encodings");
            }

            Thread.Sleep(500);
        }
    }

    private async Task Receiver(Encoding enc, ChannelWriter<GCFrame> audioWriter, CancellationToken appToken)
    {
        if (_sharedMemory is null) return;
        Console.WriteLine("Receiving for encoding...");
        while (!appToken.IsCancellationRequested)
        {
            (GCFrame[] Frames, bool success) = _sharedMemory.BlockingPopFrames(enc, 120, 2000);

            if (success && Frames.Length > 0)
            {
                foreach (GCFrame f in Frames)
                {
                    await audioWriter.WriteAsync(f, appToken);
                }
            }
            else if (success)
            {
                Console.WriteLine("Got frames successfully, but no frames were present.");
            }
            else
            {
                Console.WriteLine("Failed to get frames.");
            }
        }

    }
}