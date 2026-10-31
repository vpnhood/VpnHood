using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Streams;

namespace VpnHood.Test.KestrelParity.Servers;

internal sealed class BufferedQuicStream(Stream stream, IQuicStream quic, byte[] prefix, Action onPrefixConsumed,
    bool finish)
    : StreamDecoratorAsync(stream, leaveOpen: false), IQuicStream
{
    private ReadOnlyMemory<byte> _prefix = prefix;
    private Action? _onPrefixConsumed = onPrefixConsumed;

    public long StreamId => quic.StreamId;
    public Task ReadsClosed => quic.ReadsClosed;
    public Task WritesClosed => quic.WritesClosed;
    public override bool? DataAvailable => !_prefix.IsEmpty ? true : base.DataAvailable;

    public void AbortRead(long errorCode) => quic.AbortRead(errorCode);
    public void AbortWrite(long errorCode) => quic.AbortWrite(errorCode);
    public void CompleteWrites() => quic.CompleteWrites();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_prefix.IsEmpty) {
            // The next read proves the parser consumed its last buffered fields, not merely copied them.
            Interlocked.Exchange(ref _onPrefixConsumed, null)?.Invoke();
            return finish ? ValueTask.FromResult(0) : SourceStream.ReadAsync(buffer, cancellationToken);
        }
        var count = Math.Min(buffer.Length, _prefix.Length);
        _prefix[..count].CopyTo(buffer);
        _prefix = _prefix[count..];
        return ValueTask.FromResult(count);
    }
}
