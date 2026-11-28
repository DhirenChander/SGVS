using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SGVS.Frontend.Models;

namespace SGVS.Frontend.Services.Video;

// shows decoded frames on a participant's tile and the Watch page by writing them into WriteableBitmaps and
// setting Participant.StreamPreview.
//
// tracksActivity is for someone else's stream: they count as streaming from their first frame and stop 2 s after
// their last one (the network gives no other sign). your own stream is switched on and off by StartStream /
// StopStream, so there it only draws.
public sealed class ParticipantVideoSink : IVideoFrameSink
{
    private static readonly PixelSize Size = new(VideoFormat.Width, VideoFormat.Height);
    private static readonly Vector Dpi = new(96, 96);

    // how often a remote stream is checked for going quiet
    private static readonly TimeSpan IdleCheckPeriod = TimeSpan.FromMilliseconds(500);

    private readonly Participant _participant;
    private readonly Action<Action> _post;
    private readonly bool _tracksActivity;
    private readonly Func<TimeSpan> _clock;
    private readonly Timer? _idleTimer;
    private Action? _onFirstFrame;

    private readonly object _lock = new();
    private readonly byte[] _pending = new byte[VideoFormat.FrameBytes];
    private bool _hasPending;
    private bool _updatePosted;
    private bool _disposed;
    private TimeSpan? _lastFrameAt;

    // UI thread only
    private readonly WriteableBitmap?[] _bitmaps = new WriteableBitmap?[2];
    private int _next;

    // onFirstFrame runs once, on the UI thread, right after the first frame is on the tile. your own tile uses it to
    // keep that frame as a still and stop decoding
    public ParticipantVideoSink(Participant participant, Action<Action> post, bool tracksActivity, Func<TimeSpan>? clock = null,
        Action? onFirstFrame = null)
    {
        _participant = participant;
        _post = post;
        _tracksActivity = tracksActivity;
        _onFirstFrame = onFirstFrame;

        var watch = Stopwatch.StartNew();
        _clock = clock ?? (() => watch.Elapsed);

        if (tracksActivity)
        {
            // the idle clock starts now, not at the first frame: a sink that replaced one whose decoder died may
            // never get a frame, and the old picture must not stay up forever
            _lastFrameAt = _clock();
            _idleTimer = new Timer(_ => CheckIdle(), null, IdleCheckPeriod, IdleCheckPeriod);
        }
    }

    // called on the decoder's thread. the newest frame wins: a frame still waiting for the UI is overwritten
    public void Present(ReadOnlySpan<byte> bgra)
    {
        if (bgra.Length != VideoFormat.FrameBytes) return;

        bool post;
        lock (_lock)
        {
            if (_disposed) return;
            bgra.CopyTo(_pending);
            _hasPending = true;
            _lastFrameAt = _clock();
            post = !_updatePosted;
            _updatePosted = true;
        }

        if (post) _post(ApplyPending);
    }

    // clears a remote streamer that went quiet. called by the timer, and directly by tests
    public void CheckIdle()
    {
        if (!_tracksActivity) return;

        lock (_lock)
        {
            if (_disposed || _lastFrameAt is not { } last || _clock() - last <= VideoFormat.IdleTimeout) return;
            _lastFrameAt = null; // once is enough, the next frame starts it again
        }

        _post(() =>
        {
            lock (_lock)
            {
                // a frame landed between the check and now
                if (_disposed || _lastFrameAt is not null) return;
            }

            _participant.IsStreamer = false;
            _participant.StreamPreview = null;
        });
    }

    private void ApplyPending()
    {
        lock (_lock)
        {
            _updatePosted = false;
            if (_disposed || !_hasPending) return;
        }

        var bitmap = _bitmaps[_next] ??= new WriteableBitmap(Size, Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        _next = 1 - _next;

        using (var locked = bitmap.Lock())
        {
            lock (_lock)
            {
                // rows can be padded, so copy them one at a time
                var rowBytes = VideoFormat.Width * 4;
                for (var y = 0; y < VideoFormat.Height; y++)
                    Marshal.Copy(_pending, y * rowBytes, locked.Address + y * locked.RowBytes, rowBytes);
                _hasPending = false;
            }
        }

        // a different bitmap each frame is what makes the Image redraw
        _participant.StreamPreview = bitmap;
        if (_tracksActivity) _participant.IsStreamer = true;

        var first = _onFirstFrame;
        _onFirstFrame = null;
        first?.Invoke();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _idleTimer?.Dispose();
    }
}
