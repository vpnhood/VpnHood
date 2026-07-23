using Network;

namespace VpnHood.Core.Quic.Ios;

internal sealed class IosQuicStreamWriteOperation
{
    private int _outstandingDisposed;
    private IosQuicStream? _owner;

    public IosQuicStreamWriteOperation(IosQuicStream owner, int count, CancellationToken cancellationToken)
    {
        _owner = owner;
        CancellationToken = cancellationToken;
        Count = count;
        Toolkit.Memory.VhTypeTracker.Track(this);
        Callback = error => {
            var liveOwner = _owner;
            if (liveOwner != null) {
                Toolkit.Memory.VhTypeTracker.Record("IosQuicStreamWriteOperation.callback");
                liveOwner.OnWriteCompleted(this, error);
            }
            else {
                Toolkit.Memory.VhTypeTracker.Record("IosQuicStreamWriteOperation.callbackAfterClear");
                error?.Dispose();
            }
        };
        // Diagnostic in-flight-send counter (sendQ=), maintained only when IosQuicDiagnostics.Enabled.
        IosQuicDiagnostics.AddOutstandingSend(Count);
    }

    public IosQuicStream? Owner => _owner;
    public int Count { get; }
    public CancellationToken CancellationToken { get; }
    public ReusableValueTaskSource Source { get; } = new();
    public CancellationTokenRegistration Registration;
    public Action<NWError?> Callback { get; }

    public void DisposeOutstandingSend()
    {
        if (Interlocked.Exchange(ref _outstandingDisposed, 1) != 0)
            return;

        IosQuicDiagnostics.SubtractOutstandingSend(Count);
    }

    public void Clear()
    {
        _owner = null;
        Toolkit.Memory.VhTypeTracker.Record("IosQuicStreamWriteOperation.cleared");
    }
}
