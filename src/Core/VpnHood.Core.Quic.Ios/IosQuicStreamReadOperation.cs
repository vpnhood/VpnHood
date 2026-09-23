using Network;

namespace VpnHood.Core.Quic.Ios;

internal sealed class IosQuicStreamReadOperation
{
    private IosQuicStream? _owner;

    public IosQuicStreamReadOperation(IosQuicStream owner, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        _owner = owner;
        Buffer = buffer;
        CancellationToken = cancellationToken;
        Toolkit.Memory.VhTypeTracker.Track(this);
        Callback = (data, context, isComplete, error) => {
            var liveOwner = _owner;
            if (liveOwner != null) {
                Toolkit.Memory.VhTypeTracker.Record("IosQuicStreamReadOperation.callback");
                liveOwner.OnReadCompleted(this, data, context, isComplete, error);
            }
            else {
                Toolkit.Memory.VhTypeTracker.Record("IosQuicStreamReadOperation.callbackAfterClear");
                context?.Dispose();
                error?.Dispose();
            }
        };
    }

    public IosQuicStream? Owner => _owner;
    public Memory<byte> Buffer { get; }
    public CancellationToken CancellationToken { get; }
    public ReusableValueTaskSource<int> Source { get; } = new();
    public CancellationTokenRegistration Registration;
    public NWConnectionReceiveDispatchDataCompletion Callback { get; }

    public void Clear()
    {
        _owner = null;
        Toolkit.Memory.VhTypeTracker.Record("IosQuicStreamReadOperation.cleared");
    }
}
