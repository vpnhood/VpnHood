using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Streams;

namespace VpnHood.Core.Server.ProbeResistance;

// One buffer for the HTTP connection, including pipelined requests. The tunnel inherits this stream so
// bytes read past its headers are replayed. Ordinary ReadAsync never reads ahead after that handoff.
internal sealed class HttpReadStream(Stream sourceStream) : StreamDecoratorAsync(sourceStream, leaveOpen: true)
{
    private readonly byte[] _buffer = new byte[16 * 1024];
    private int _offset;
    private int _count;

    public ReadOnlySpan<byte> BufferedSpan => _buffer.AsSpan(_offset, _count);
    public override bool CanSeek => false;
    public override bool? DataAvailable => _count > 0 ? true : base.DataAvailable;

    public void Consume(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _count);
        _offset += count;
        _count -= count;
    }

    public ValueTask<bool> EnsureBuffered(int minimum, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThan(minimum, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimum, _buffer.Length);
        return _count >= minimum ? ValueTask.FromResult(true) : Fill(minimum, cancellationToken);
    }

    private async ValueTask<bool> Fill(int minimum, CancellationToken cancellationToken)
    {
        if (_count > 0)
            _buffer.AsSpan(_offset, _count).CopyTo(_buffer);
        _offset = 0;
        while (_count < minimum) {
            var read = await SourceStream.ReadAsync(_buffer.AsMemory(_count), cancellationToken).Vhc();
            if (read == 0)
                return false;
            _count += read;
        }
        return true;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_count == 0)
            return SourceStream.ReadAsync(buffer, cancellationToken);

        var count = Math.Min(buffer.Length, _count);
        BufferedSpan[..count].CopyTo(buffer.Span);
        Consume(count);
        return ValueTask.FromResult(count);
    }
}
