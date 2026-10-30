using System.Net.Quic;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Quic.MsQuic;

// A transport adapter only: no buffering, framing or application protocol decisions belong here.
internal sealed class MsQuicServerStream(QuicStream stream) : Stream, IQuicStream
{
    private readonly AsyncLock _disposeLock = new();
    private bool _disposed;

    public long StreamId => stream.Id;
    public Task ReadsClosed => stream.ReadsClosed;
    public Task WritesClosed => stream.WritesClosed;
    public override bool CanRead => stream.CanRead;
    public override bool CanWrite => stream.CanWrite;
    public override bool CanSeek => stream.CanSeek;
    public override bool CanTimeout => stream.CanTimeout;
    public override long Length => stream.Length;
    public override long Position { get => stream.Position; set => stream.Position = value; }
    public override int ReadTimeout { get => stream.ReadTimeout; set => stream.ReadTimeout = value; }
    public override int WriteTimeout { get => stream.WriteTimeout; set => stream.WriteTimeout = value; }

    public void AbortRead(long errorCode) => stream.Abort(QuicAbortDirection.Read, errorCode);
    public void AbortWrite(long errorCode) => stream.Abort(QuicAbortDirection.Write, errorCode);
    public void CompleteWrites() => stream.CompleteWrites();
    public override void Flush() => stream.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => stream.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);
    public override void SetLength(long value) => stream.SetLength(value);
    public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => stream.Read(buffer);
    public override int ReadByte() => stream.ReadByte();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        stream.ReadAsync(buffer, offset, count, cancellationToken);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        stream.ReadAsync(buffer, cancellationToken);
    public override void Write(byte[] buffer, int offset, int count) => stream.Write(buffer, offset, count);
    public override void Write(ReadOnlySpan<byte> buffer) => stream.Write(buffer);
    public override void WriteByte(byte value) => stream.WriteByte(value);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        stream.WriteAsync(buffer, offset, count, cancellationToken);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        stream.WriteAsync(buffer, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        // QuicStream.Dispose waits for its peer. A synchronous close must not hold up server shutdown.
        if (disposing)
            _ = this.TryDisposeAsync();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        using var scopeLock = await _disposeLock.LockAsync(CancellationToken.None).Vhc();
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        // The native close may outlive this deadline; it owns its remaining resources until it finishes.
        await stream.TryDisposeAsync(TimeSpan.FromSeconds(5)).Vhc();
        GC.SuppressFinalize(this);
    }
}
