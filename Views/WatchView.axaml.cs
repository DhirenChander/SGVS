using Avalonia.Controls;
using LibVLCSharp.Shared;
using SVGS.Frontend.ViewModels;

namespace SVGS.Frontend.Views;

public partial class WatchView : UserControl
{
    private LibVLC? _libVlc;
    private MediaPlayer? _player;

    public WatchView()
    {
        InitializeComponent();

        // the video surface needs a real window first, so wait for Loaded
        Loaded += (_, _) => StartPlaceholderVideo();
        Unloaded += (_, _) => StopVideo();
    }

    // loops the first mp4 in StreamVideo/ until there's a real stream.
    // no mp4 = the image stays up
    private void StartPlaceholderVideo()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "StreamVideo");
        var file = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.mp4").Order().FirstOrDefault()
            : null;
        if (file is null) return;

        try
        {
            // throws if the vlc libs are missing (e.g. no VLC installed on linux)
            Core.Initialize();
            _libVlc = new LibVLC();
            _player = new MediaPlayer(_libVlc);

            if (DataContext is WatchViewModel vm)
            {
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(WatchViewModel.Volume) or nameof(WatchViewModel.IsStreamMuted))
                        ApplyVolume(vm);
                };

                // vlc resets the volume when playback starts, so set it again then
                _player.Playing += (_, _) => ApplyVolume(vm);
                ApplyVolume(vm);
            }

            using var media = new Media(_libVlc, file, FromType.FromPath);
            media.AddOption(":input-repeat=65535"); // loop

            VideoSurface.MediaPlayer = _player;
            VideoSurface.IsVisible = true;
            StreamImage.IsVisible = false;
            _player.Play(media);
        }
        catch (Exception)
        {
            // couldn't start vlc, fall back to the image
            StopVideo();
            VideoSurface.IsVisible = false;
            StreamImage.IsVisible = true;
        }
    }

    // can be called from vlc's thread too
    private void ApplyVolume(WatchViewModel vm)
    {
        if (_player is null) return;
        _player.Mute = vm.IsStreamMuted;
        _player.Volume = (int)Math.Round(vm.Volume);
    }

    private void StopVideo()
    {
        _player?.Stop();
        VideoSurface.MediaPlayer = null;
        _player?.Dispose();
        _libVlc?.Dispose();
        _player = null;
        _libVlc = null;
    }
}