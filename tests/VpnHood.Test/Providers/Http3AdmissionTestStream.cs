namespace VpnHood.Test.Providers;

internal sealed class Http3AdmissionTestStream(IReadOnlyList<byte> bytes) : MemoryStream(bytes.ToArray())
{
    public int ReadCount { get; private set; }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.IsEmpty)
            return ValueTask.FromResult(0);
        // A read after the supplied prefix would stall on a live transport; fail immediately instead.
        if (Position == Length)
            throw new InvalidOperationException("Admission waited beyond the available frame boundary.");
        ReadCount++;
        return base.ReadAsync(buffer[..1], cancellationToken);
    }
}
