using System.Diagnostics;

namespace SGVS.Frontend.Services.Video;

// moves a stream from its source through the decoder to the sink, on its own background thread. the same
// pipeline shows your own stream and everyone else's, only the source differs.
// Start when there is a stream to show, Stop when it ends: Stop also disposes the decoder and the sink
public sealed class IncomingVideoPipeline(IEncodedVideoSource source, IVideoDecoder decoder, IVideoFrameSink sink)
    : IDisposable
{
    // how long each read waits, so Stop returns within about this long
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(10);

    private readonly object _lifecycle = new();
    private Thread? _thread;
    private volatile bool _stopping;
    private bool _stopped;
    private int _accessUnits, _errors;

    public int AccessUnits => Volatile.Read(ref _accessUnits);

    // exceptions caught from the source or the decoder
    public int Errors => Volatile.Read(ref _errors);

    public void Start()
    {
        lock (_lifecycle)
        {
            if (_thread is not null || _stopped) return;
            decoder.FrameDecoded += OnFrame;
            _thread = new Thread(Run) { IsBackground = true, Name = "Incoming video" };
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_lifecycle)
        {
            if (_stopped) return;
            _stopped = true;
            _stopping = true;
            thread = _thread;
            _thread = null;
        }

        // dispose the decoder first: a decoder stuck writing to an ffmpeg that stopped reading only lets go once
        // it is disposed (FfmpegVideoDecoder kills the process), and the thread can't finish until then
        decoder.Dispose();
        if (thread is not null && thread != Thread.CurrentThread) thread.Join();
        decoder.FrameDecoded -= OnFrame;
        sink.Dispose();
    }

    public void Dispose() => Stop();

    private void OnFrame(byte[] bgra) => sink.Present(bgra);

    private void Run()
    {
        while (!_stopping)
        {
            var clock = Stopwatch.StartNew();
            try
            {
                if (source.TryReadAccessUnit(ReadTimeout, out var accessUnit))
                {
                    Interlocked.Increment(ref _accessUnits);
                    decoder.Decode(accessUnit.Span);
                    continue;
                }
            }
            catch (Exception e)
            {
                Interlocked.Increment(ref _errors);
                Trace.WriteLine("[video] " + e.Message);
            }

            // a source that is finished or broken returns at once, so back off a little instead of spinning.
            // only a little: a live source can fail a read just after its data landed, and waiting out the
            // whole read would let frames pile up and play as a burst
            var left = ReadTimeout - clock.Elapsed;
            if (left > TimeSpan.Zero) Thread.Sleep(left < RetryDelay ? left : RetryDelay);
        }
    }
}
