using Network;

namespace VpnHood.Core.Quic.Ios;

internal sealed class IosQuicStreamReadOperation
{
    public IosQuicStreamReadOperation(IosQuicStream owner, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        Owner = owner;
        Buffer = buffer;
        CancellationToken = cancellationToken;
        Callback = (data, context, isComplete, error) =>
            owner.OnReadCompleted(this, data, context, isComplete, error);
    }

    public IosQuicStream Owner { get; }
    public Memory<byte> Buffer { get; }
    public CancellationToken CancellationToken { get; }
    public ReusableValueTaskSource<int> Source { get; } = new();
    public CancellationTokenRegistration Registration;
    public NWConnectionReceiveReadOnlySpanCompletion Callback { get; }
}
