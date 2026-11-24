namespace VpnHood.Test.Providers.Proxies;

// a stream that counts the bytes written through it
internal sealed class CountingStream(Stream inner, Action<int> onWrite) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        inner.ReadAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        onWrite(count);
        inner.Write(buffer, offset, count);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        onWrite(buffer.Length);
        return inner.WriteAsync(buffer, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }
}
