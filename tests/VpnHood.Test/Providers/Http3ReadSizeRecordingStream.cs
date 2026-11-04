namespace VpnHood.Test.Providers;

internal sealed class Http3ReadSizeRecordingStream(int length) : MemoryStream(new byte[length])
{
    public List<int> ReadSizes { get; } = [];

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ReadSizes.Add(buffer.Length);
        return base.ReadAsync(buffer, cancellationToken);
    }
}
