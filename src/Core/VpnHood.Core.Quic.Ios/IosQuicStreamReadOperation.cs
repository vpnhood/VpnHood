using CoreFoundation;
using System;
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
        VpnHood.Core.Toolkit.Memory.VhTypeTracker.Track(this);
        Callback = (data, context, isComplete, error) => {
            var liveOwner = _owner;
            if (liveOwner != null) {
                VpnHood.Core.Toolkit.Memory.VhTypeTracker.Record("IosQuicStreamReadOperation.callback");
                liveOwner.OnReadCompleted(this, data, context, isComplete, error);
            }
            else {
                VpnHood.Core.Toolkit.Memory.VhTypeTracker.Record("IosQuicStreamReadOperation.callbackAfterClear");
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
        VpnHood.Core.Toolkit.Memory.VhTypeTracker.Record("IosQuicStreamReadOperation.cleared");
    }
}
