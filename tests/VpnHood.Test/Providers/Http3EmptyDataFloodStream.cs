namespace VpnHood.Test.Providers;

internal sealed class Http3EmptyDataFloodStream(byte[] prefix) : MemoryStream(prefix)
{
    public int FloodBytesProvided { get; private set; }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Position < Length)
            return base.ReadAsync(buffer, cancellationToken);
        // Bound a regression without allocating a large request body or hanging the test.
        if (FloodBytesProvided >= 128 * 1024)
            throw new InvalidOperationException("The available-body inspection did not stop.");
        buffer.Span.Clear();
        FloodBytesProvided += buffer.Length;
        return new ValueTask<int>(buffer.Length);
    }
}
