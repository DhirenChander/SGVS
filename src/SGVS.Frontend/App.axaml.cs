using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SGVS.Frontend.ViewModels;
using SGVS.Frontend.Views;
using SGVS.Frontend.Services.Audio;
using SGVS.Frontend.Services.Video;
using System.Threading.Channels;
using SharedMemLibrary;

namespace SGVS.Frontend;

public partial class App : Application
{
    private CancellationTokenSource? _appCts;
    private IFrameBuffer? _sharedMemory;
    private IPcmSink? _pcmSink;
    private MainViewModel? _main;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _pcmSink = new PcmPlayer();
            _sharedMemory = new GCFrameBuffer();
            var main = new MainViewModel(_sharedMemory);
            _main = main;
            desktop.MainWindow = new MainWindow
            {
                DataContext = main,
            };

            Channel<GCFrame> audioChan = Channel.CreateBounded<GCFrame>(200); // Holds 2 seconds of audio

            _appCts = new CancellationTokenSource();
            var appToken = _appCts.Token;

            // 1. Start the app-lifetime background thread here
            StartBackgroundWorkers(appToken);

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

    private void StartBackgroundWorkers(CancellationToken appToken)
    {
        Task.Factory.StartNew(() => EncodingWatcher(appToken), cancellationToken: appToken);

        Task.Factory.StartNew(MessageWatcher, cancellationToken: appToken);
    }

    private void MessageWatcher()
    {

    }

    private void EncodingWatcher(CancellationToken appToken)
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
                        StartReceiver(enc, appToken);
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

    private void StartReceiver(Encoding _enc, CancellationToken _appToken)
    {
        // someone's screen stream. the field is read directly, IsVideo() would call into the native library
        if (_enc.type == EncodingType.Video)
        {
            var source = new SharedMemoryVideoSource(_sharedMemory!, _enc);
            Dispatcher.UIThread.Post(() => _main?.AttachRemoteVideo(source));
            return;
        }

        SharedMemoryAudioSource audioSource = new SharedMemoryAudioSource(_sharedMemory!);
        IncomingAudioPump audioPump = new IncomingAudioPump(audioSource, _pcmSink!);
        audioPump.Start();
        var participantName = $"Participant-{_enc.id[0]:x8}";
        Dispatcher.UIThread.Post(() => _main?.NotifyParticipantJoined(participantName));
    }
}