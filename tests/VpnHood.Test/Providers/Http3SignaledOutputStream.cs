namespace VpnHood.Test.Providers;

internal sealed class Http3SignaledOutputStream(Http3RequestTestGate gate) : MemoryStream
{
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var write = base.WriteAsync(buffer, cancellationToken);
        gate.ResponseSubmitted();
        return write;
    }
}
