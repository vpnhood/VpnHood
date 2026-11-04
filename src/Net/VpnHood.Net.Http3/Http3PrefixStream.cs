using System.Buffers;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Streams;

namespace VpnHood.Net.Http3;

// The header buffer belongs to this wrapper until its final replayed byte. A tunnel thereafter
// reads straight from QUIC, with no buffer retained and no lock on its steady-state reads.
internal sealed class Http3PrefixStream(Stream source, byte[] prefix, int count)
    : StreamDecoratorAsync(source, leaveOpen: true)
{
    private readonly Lock _prefixLock = new();
    private byte[]? _prefix = prefix;
    private int _offset;

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _prefix) != null) {
            lock (_prefixLock) {
                if (_prefix != null) {
                    var length = Math.Min(buffer.Length, count - _offset);
                    _prefix.AsMemory(_offset, length).CopyTo(buffer);
                    _offset += length;
                    if (_offset == count)
                        ReturnPrefix();
                    if (length != 0 || buffer.Length == 0)
                        return ValueTask.FromResult(length);
                }
            }
        }
        return SourceStream.ReadAsync(buffer, cancellationToken);
    }

    private void ReturnPrefix()
    {
        var rented = Interlocked.Exchange(ref _prefix, null);
        if (rented != null)
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
    }

    public override async ValueTask DisposeAsync()
    {
        lock (_prefixLock)
            ReturnPrefix();
        await base.DisposeAsync().Vhc();
    }

    protected override void Dispose(bool disposing)
    {
        lock (_prefixLock)
            ReturnPrefix();
        base.Dispose(disposing);
    }
}
