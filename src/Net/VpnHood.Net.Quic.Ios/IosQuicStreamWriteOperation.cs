using Network;

namespace VpnHood.Net.Quic.Ios;

internal sealed class IosQuicStreamWriteOperation
{
    private int _outstandingDisposed;
    private readonly long _countedSendBytes; // what the in-flight counter took for this send, to give back at its end
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
        // Diagnostic in-flight-send counter (sendQ=), counted only when IosQuicDiagnostics.Enabled.
        _countedSendBytes = IosQuicDiagnostics.AddOutstandingSend(Count);
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

        IosQuicDiagnostics.SubtractOutstandingSend(_countedSendBytes);
    }

    public void Clear()
    {
        _owner = null;
        Toolkit.Memory.VhTypeTracker.Record("IosQuicStreamWriteOperation.cleared");
    }
}
