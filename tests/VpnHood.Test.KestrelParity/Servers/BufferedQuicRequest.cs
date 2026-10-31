using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Test.KestrelParity.Servers;

internal sealed class BufferedQuicRequest(BufferedQuicServer server, int byteCount,
    Action onPrefixConsumed, bool finish, CancellationTokenSource cancellation) : IDisposable
{
    private int _claimed;
    private int _disposed;

    public int ByteCount => byteCount;
    public Action OnPrefixConsumed => onPrefixConsumed;
    public bool Finish => finish;
    public CancellationToken CancellationToken { get; } = cancellation.Token;

    public bool TryClaim() => !CancellationToken.IsCancellationRequested &&
                              Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        // A failed connect must release only its own reservation; an active prefetch must stop too.
        server.ReleaseRequest(this);
        cancellation.TryCancel();
        cancellation.Dispose();
    }
}
