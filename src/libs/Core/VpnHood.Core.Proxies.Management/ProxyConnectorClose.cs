using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Proxies.Management;

// The connector's close, which its parts look at: nothing new starts once it began, and its token stops what runs. Its
// source is not disposed: it has no timer, and the stop may come at any time
internal sealed class ProxyConnectorClose
{
    private readonly CancellationTokenSource _cts = new();
    private bool _isClosed;

    public bool IsClosed => _isClosed;

    public CancellationToken Token => _cts.Token;

    // false where it had begun already
    public bool TryBegin() => !Interlocked.Exchange(ref _isClosed, true);

    public void Stop() => _cts.TryCancel();

    // as the connector's, whose close it is
    public void ThrowIfClosed() => ObjectDisposedException.ThrowIf(_isClosed, typeof(ManagedProxyConnector));
}
