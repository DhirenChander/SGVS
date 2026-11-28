namespace SGVS.Frontend.Services.Video;

// cuts an Annex-B H.264 byte stream into access units (one per frame). the encoder is told to start every
// access unit with an access unit delimiter NAL (00 00 00 01 09), so that is where each one ends.
// not thread-safe
public sealed class AnnexBAccessUnitSplitter
{
    // an access unit is at most one 500 KB shared memory slot, so a buffer this big with no delimiter is garbage
    public const int MaxBufferedBytes = 2 * 1024 * 1024;

    private static readonly byte[] Delimiter = [0, 0, 0, 1, 9];

    private byte[] _buffer = new byte[64 * 1024];
    private int _length;
    private int _scanFrom;
    private bool _started;

    // every access unit completed by this chunk. the last one stays buffered until the next delimiter shows
    public List<byte[]> Push(ReadOnlySpan<byte> chunk)
    {
        var done = new List<byte[]>();
        Append(chunk);

        while (true)
        {
            var found = IndexOfDelimiter(_scanFrom);
            if (found < 0)
            {
                // a delimiter may be cut across chunks, so look again from just before the end next time
                _scanFrom = Math.Max(0, _length - (Delimiter.Length - 1));
                break;
            }

            if (_started)
                done.Add(_buffer.AsSpan(0, found).ToArray());

            // drop what was emitted, or the junk before the first delimiter
            Consume(found);
            _started = true;
            _scanFrom = Delimiter.Length;
        }

        if (!_started)
        {
            // still no first delimiter: nothing buffered is usable except the end, which may be half of one
            var keep = Math.Min(_length, Delimiter.Length - 1);
            Consume(_length - keep);
            _scanFrom = 0;
        }
        else if (_length > MaxBufferedBytes)
        {
            Reset();
        }

        return done;
    }

    // the access unit still being built, or null. call when the stream ends
    public byte[]? Flush()
    {
        var last = _started && _length > 0 ? _buffer.AsSpan(0, _length).ToArray() : null;
        Reset();
        return last;
    }

    private void Reset()
    {
        _length = 0;
        _scanFrom = 0;
        _started = false;
    }

    private void Append(ReadOnlySpan<byte> chunk)
    {
        if (_length + chunk.Length > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + chunk.Length));
        chunk.CopyTo(_buffer.AsSpan(_length));
        _length += chunk.Length;
    }

    private int IndexOfDelimiter(int from)
    {
        if (from >= _length) return -1;
        var at = _buffer.AsSpan(from, _length - from).IndexOf(Delimiter);
        return at < 0 ? -1 : from + at;
    }

    private void Consume(int count)
    {
        Buffer.BlockCopy(_buffer, count, _buffer, 0, _length - count);
        _length -= count;
    }
}
