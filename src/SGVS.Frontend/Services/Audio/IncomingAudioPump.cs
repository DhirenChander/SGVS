namespace SGVS.Frontend.Services.Audio;

// Moves incoming call audio from the source through the decoder to the sink, on its own background thread.
// Start when the call connects; Stop (or Dispose) when it ends, before disposing whatever the source reads from.
public sealed class IncomingAudioPump(IIncomingAudioSource source, IPcmSink sink, Action<TimeSpan>? sleep = null)
    : IDisposable
{
    // how long each read waits, so Stop returns within about this long
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromMilliseconds(100);

    private readonly OpusPacketDecoder _decoder = new();
    private readonly byte[] _pcm = new byte[OpusPacketDecoder.PcmBytes];
    private readonly Action<TimeSpan> _sleep = sleep ?? Thread.Sleep;
    private readonly object _lifecycle = new();

    private Thread? _thread;
    private volatile bool _stopping;
    private int _decoded, _dropped, _errors;
    private Exception? _lastError;

    public int PacketsDecoded => Volatile.Read(ref _decoded);
    public int PacketsDropped => Volatile.Read(ref _dropped); // packets that didn't decode
    public int Errors => Volatile.Read(ref _errors);          // exceptions caught from the source, decoder or sink
    public Exception? LastError => Volatile.Read(ref _lastError);

    // Starts the background thread. Does nothing if it's already running.
    public void Start()
    {
        lock (_lifecycle)
        {
            if (_thread is not null)
                return;
            _stopping = false;
            _thread = new Thread(Run) { IsBackground = true, Name = "Incoming audio" };
            _thread.Start();
        }
    }

    // Stops the thread and waits for it to finish its current read. Safe to call more than once.
    public void Stop()
    {
        Thread? thread;
        lock (_lifecycle)
        {
            thread = _thread;
            _thread = null;
            _stopping = true;
        }
        // the sink could call Stop from the pump thread itself; joining there would deadlock
        if (thread is not null && thread != Thread.CurrentThread)
            thread.Join();
    }

    public void Dispose() => Stop();

    // One read, decode and Enqueue. False if no packet came.
    // Never throws: a failure is counted, kept in LastError, and waited out so a lasting fault doesn't spin.
    public bool PumpOnce(TimeSpan timeout)
    {
        try
        {
            if (!source.TryReadPacket(timeout, out var packet))
                return false;

            if (_decoder.TryDecode(packet.Span, _pcm))
            {
                sink.Enqueue(_pcm);
                Interlocked.Increment(ref _decoded);
            }
            else
            {
                Interlocked.Increment(ref _dropped);
            }
            return true;
        }
        catch (Exception e)
        {
            Interlocked.Increment(ref _errors);
            Volatile.Write(ref _lastError, e);
            _sleep(timeout);
            return false;
        }
    }

    private void Run()
    {
        while (!_stopping)
            PumpOnce(ReadTimeout);
    }
}
