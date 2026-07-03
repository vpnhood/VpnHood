using Network;

namespace VpnHood.Core.Quic.Ios;

internal sealed class IosQuicStreamWriteOperation
{
    private int _outstandingDisposed;

    public IosQuicStreamWriteOperation(IosQuicStream owner, int count, CancellationToken cancellationToken)
    {
        Owner = owner;
        CancellationToken = cancellationToken;
        Count = count;
        Callback = error => owner.OnWriteCompleted(this, error);
        Interlocked.Add(ref IosQuicClient.OutstandingSendBytes, Count);
    }

    public IosQuicStream Owner { get; }
    public int Count { get; }
    public CancellationToken CancellationToken { get; }
    public ReusableValueTaskSource Source { get; } = new();
    public CancellationTokenRegistration Registration;
    public Action<NWError?> Callback { get; }

    public void DisposeOutstandingSend()
    {
        if (Interlocked.Exchange(ref _outstandingDisposed, 1) != 0)
            return;

        Interlocked.Add(ref IosQuicClient.OutstandingSendBytes, -Count);
    }
}
