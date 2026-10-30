namespace VpnHood.Test.KestrelParity.Probing;

// Passes a connection's bytes through and notes the TLS records that arrive after the handshake, by type and size,
// so a probe sees how the peer ended: in TLS 1.2 a close_notify is an alert record; in TLS 1.3 it is encrypted, and
// only a 19-byte record's size suggests it.
internal sealed class TapStream(Stream inner) : Stream
{
    private readonly List<string> _records = [];
    private readonly byte[] _header = new byte[5];
    private int _headerFill;
    private int _bodyLeft;
    private bool _handshakeDone;
    private MemoryStream? _bufferedWrites;

    public void BufferWrites() => _bufferedWrites = new MemoryStream();

    // Encrypt the whole of a long request before TCP gets any of it: a send that fails part way must not fault
    // SslStream's cipher state before its reader has taken a rejection the server already wrote.
    public async Task FlushBufferedWrites(CancellationToken cancellationToken)
    {
        using var buffered = _bufferedWrites;
        _bufferedWrites = null;
        if (buffered != null)
            await inner.WriteAsync(buffered.GetBuffer().AsMemory(0, (int)buffered.Length), cancellationToken);
    }

    public void MarkHandshakeDone()
    {
        lock (_records) {
            _handshakeDone = true;
            _records.Clear();
        }
    }

    // the records after the last application data the peer sent, and "cut" when the connection ended inside one, as
    // a record's header gets noted before its body comes
    public string TailRecords()
    {
        lock (_records) {
            return _bodyLeft > 0 || _headerFill > 0
                ? string.Join(",", _records.Append("cut"))
                : string.Join(",", _records);
        }
    }

    private void Note(ReadOnlySpan<byte> data)
    {
        lock (_records) {
            var i = 0;
            while (i < data.Length) {
                if (_bodyLeft > 0) {
                    var take = Math.Min(_bodyLeft, data.Length - i);
                    _bodyLeft -= take;
                    i += take;
                    continue;
                }

                _header[_headerFill++] = data[i++];
                if (_headerFill < 5) continue;
                _headerFill = 0;
                _bodyLeft = (_header[3] << 8) | _header[4];
                if (!_handshakeDone) continue;
                // keep only what follows the last sizable data record, which is the reply itself
                if (_header[0] == 23 && _bodyLeft > 19) _records.Clear();
                _records.Add($"{_header[0]}:{_bodyLeft}");
            }
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        Note(buffer.Span[..read]);
        return read;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        Note(buffer.AsSpan(offset, read));
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => (_bufferedWrites ?? inner).WriteAsync(buffer, cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) => (_bufferedWrites ?? inner).Write(buffer, offset, count);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => (_bufferedWrites ?? inner).WriteAsync(buffer, offset, count, cancellationToken);

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
