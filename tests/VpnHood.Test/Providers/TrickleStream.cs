namespace VpnHood.Test.Providers;

// A stream that gives one byte at a time, as a network may; what is written to it goes after its data
internal sealed class TrickleStream : MemoryStream
{
    public TrickleStream(byte[] data)
    {
        Write(data);
        Position = 0;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        base.ReadAsync(buffer, offset, Math.Min(1, count), cancellationToken);
}
