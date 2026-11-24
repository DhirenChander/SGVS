using Avalonia;
using Avalonia.Headless.XUnit;
using SGVS.Frontend.Models;
using SGVS.Frontend.Services.Video;
using Xunit;

namespace SGVS.Frontend.Tests.Video;

// runs on the headless Avalonia app from ViewRenderTests.cs, because it makes real bitmaps
public class ParticipantVideoSinkTests
{
    private static readonly byte[] Frame = new byte[VideoFormat.FrameBytes];

    private readonly Participant _participant = new() { Name = "Pat" };
    private TimeSpan _now = TimeSpan.Zero;

    private ParticipantVideoSink Create(bool tracksActivity) =>
        new(_participant, action => action(), tracksActivity, () => _now);

    [AvaloniaFact]
    public void Present_sets_stream_preview_and_marks_streamer()
    {
        using var sink = Create(tracksActivity: true);

        sink.Present(Frame);

        Assert.True(_participant.IsStreamer);
        Assert.Equal(new PixelSize(1280, 720), _participant.StreamPreview!.PixelSize);
    }

    [AvaloniaFact]
    public void Consecutive_frames_swap_bitmaps()
    {
        using var sink = Create(tracksActivity: true);

        sink.Present(Frame);
        var first = _participant.StreamPreview;
        sink.Present(Frame);
        var second = _participant.StreamPreview;
        sink.Present(Frame);

        // a new reference each frame is what makes Avalonia redraw the Image
        Assert.NotSame(first, second);
        Assert.Same(first, _participant.StreamPreview);
    }

    [AvaloniaFact]
    public void Idle_for_two_seconds_clears_streamer_and_preview()
    {
        using var sink = Create(tracksActivity: true);
        sink.Present(Frame);

        _now = TimeSpan.FromSeconds(2.1);
        sink.CheckIdle();

        Assert.False(_participant.IsStreamer);
        Assert.Null(_participant.StreamPreview);
    }

    [AvaloniaFact]
    public void A_stream_that_is_still_sending_is_not_cleared()
    {
        using var sink = Create(tracksActivity: true);
        sink.Present(Frame);

        _now = TimeSpan.FromSeconds(1.5);
        sink.CheckIdle();

        Assert.True(_participant.IsStreamer);
        Assert.NotNull(_participant.StreamPreview);
    }

    [AvaloniaFact]
    public void Self_sink_does_not_touch_IsStreamer()
    {
        using var sink = Create(tracksActivity: false);

        sink.Present(Frame);
        _now = TimeSpan.FromSeconds(10);
        sink.CheckIdle();

        Assert.False(_participant.IsStreamer);
        Assert.NotNull(_participant.StreamPreview); // your own tile keeps its picture, StopStream clears it
    }

    [AvaloniaFact]
    public void OnFirstFrame_runs_once_after_the_first_frame_is_on_the_tile()
    {
        var calls = 0;
        var previewWhenCalled = (Avalonia.Media.Imaging.Bitmap?)null;
        using var sink = new ParticipantVideoSink(_participant, action => action(), false, () => _now,
            onFirstFrame: () => { calls++; previewWhenCalled = _participant.StreamPreview; });

        sink.Present(Frame);
        sink.Present(Frame);
        sink.Present(Frame);

        Assert.Equal(1, calls);
        Assert.NotNull(previewWhenCalled);
    }

    [AvaloniaFact]
    public void Present_after_dispose_is_ignored()
    {
        var sink = Create(tracksActivity: true);
        sink.Dispose();

        sink.Present(Frame);

        Assert.Null(_participant.StreamPreview);
        Assert.False(_participant.IsStreamer);
    }

    [AvaloniaFact]
    public void Wrong_size_frame_is_dropped()
    {
        using var sink = Create(tracksActivity: true);

        sink.Present(new byte[100]);

        Assert.Null(_participant.StreamPreview);
    }
}
