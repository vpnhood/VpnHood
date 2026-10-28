namespace VpnHood.Test.Providers;

// Bytes read forward only, as an Android asset's stream is - it cannot seek, so a ZipArchive would
// copy it whole first - counting how many were read. Every read lands in Read(byte[], int, int): a
// MemoryStream subclass's span read goes through it too.
public class TestForwardOnlyStream(byte[] bytes) : MemoryStream(bytes, writable: false)
{
    public long BytesRead { get; private set; }
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();

    public override long Position {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = base.Read(buffer, offset, count);
        BytesRead += read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromResult(Read(buffer, offset, count));

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));
}
