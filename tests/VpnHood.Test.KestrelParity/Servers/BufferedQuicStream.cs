using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Streams;

namespace VpnHood.Test.KestrelParity.Servers;

internal sealed class BufferedQuicStream(Stream stream, IQuicStream quic, byte[] prefix, int headerByteCount,
    Http3RequestGate gate, bool finish)
    : StreamDecoratorAsync(stream, leaveOpen: false), IQuicStream
{
    private ReadOnlyMemory<byte> _prefix = prefix;
    private int _headerBytesRemaining = headerByteCount;

    public long StreamId => quic.StreamId;
    public Task ReadsClosed => quic.ReadsClosed;
    public Task WritesClosed => quic.WritesClosed;
    public override bool? DataAvailable => !_prefix.IsEmpty ? true : base.DataAvailable;

    public void AbortRead(long errorCode) => quic.AbortRead(errorCode);
    public void AbortWrite(long errorCode) => quic.AbortWrite(errorCode);
    public void CompleteWrites() => quic.CompleteWrites();
    public ValueTask WriteFinal(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) =>
        quic.WriteFinal(buffer, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_prefix.IsEmpty) {
            // A buffered FIN still needs body-length validation; an open prefix releases the held application.
            if (finish)
                return ValueTask.FromResult(0);
            gate.OnPrefixConsumed();
            return SourceStream.ReadAsync(buffer, cancellationToken);
        }
        if (_headerBytesRemaining > 0) {
            var count = CopyPrefix(buffer, _headerBytesRemaining);
            _headerBytesRemaining -= count;
            return ValueTask.FromResult(count);
        }
        return ReadBody(buffer, cancellationToken);
    }

    private async ValueTask<int> ReadBody(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        // Freeze application selection before exposing body/trailers, then hold emission until parsing catches up.
        await gate.WaitForApplicationStarted(cancellationToken);
        return CopyPrefix(buffer, _prefix.Length);
    }

    private int CopyPrefix(Memory<byte> buffer, int limit)
    {
        var count = Math.Min(buffer.Length, Math.Min(limit, _prefix.Length));
        _prefix[..count].CopyTo(buffer);
        _prefix = _prefix[count..];
        return count;
    }
}
