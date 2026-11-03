using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Server.Access.Managers.HttpAccessManagers;

// Sends a server's status calls over the given address families in turn, each on a new connection: the
// access manager sees the address a call comes from, so it learns when one of the server's addresses changes.
// A family that does not connect in time gives way to the other inside the connect, before anything is sent:
// a status carries usage, and must never go twice. Every other call goes over a pooled connection, as usual.
public sealed class StatusAddressFamilyHandler : HttpMessageHandler
{
    // short of the system's own connect timeout (about 20 s on Windows, 2 minutes on Linux), as a family
    // whose packets are dropped would hold every other status that long
    private static readonly TimeSpan FamilyConnectTimeout = TimeSpan.FromSeconds(5);
    private readonly IReadOnlyList<AddressFamily> _addressFamilies;
    private readonly HttpMessageInvoker _defaultInvoker = new(new SocketsHttpHandler());
    private readonly HttpMessageInvoker _statusInvoker;
    private uint _connectCount;

    // IPv4 alone sends every status over IPv4, and over IPv6 only where IPv4 does not connect
    public StatusAddressFamilyHandler(IReadOnlyList<AddressFamily> addressFamilies)
    {
        if (addressFamilies.Count == 0 || addressFamilies.Any(x =>
                x is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)))
            throw new ArgumentException("The status calls take IPv4, IPv6 or both.", nameof(addressFamilies));

        _addressFamilies = addressFamilies.ToArray();
        _statusInvoker = new HttpMessageInvoker(new SocketsHttpHandler {
            // a kept connection would keep showing one family
            PooledConnectionLifetime = TimeSpan.Zero,
            ConnectCallback = Connect
        });
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var invoker = IsStatus(request) ? _statusInvoker : _defaultInvoker;
        return invoker.SendAsync(request, cancellationToken);
    }

    // the access manager's status route, HttpAccessManager's Server_UpdateStatus
    private static bool IsStatus(HttpRequestMessage request)
    {
        return request.Method == HttpMethod.Post &&
               request.RequestUri?.AbsolutePath.EndsWith("/agent/status", StringComparison.Ordinal) == true;
    }

    private async ValueTask<Stream> Connect(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var turn = Interlocked.Increment(ref _connectCount) - 1;
        var addressFamily = _addressFamilies[(int)(turn % (uint)_addressFamilies.Count)];

        var stream = await TryConnect(context.DnsEndPoint, addressFamily, cancellationToken).Vhc();
        if (stream != null)
            return stream;

        var otherFamily = addressFamily == AddressFamily.InterNetwork
            ? AddressFamily.InterNetworkV6
            : AddressFamily.InterNetwork;

        return await AddressFamilyConnector.Connect(context.DnsEndPoint, otherFamily, cancellationToken).Vhc();
    }

    private static async Task<Stream?> TryConnect(DnsEndPoint endPoint, AddressFamily addressFamily,
        CancellationToken cancellationToken)
    {
        try {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(FamilyConnectTimeout);
            return await AddressFamilyConnector.Connect(endPoint, addressFamily, timeoutCts.Token).Vhc();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            // as on every machine without that family
            VhLogger.Instance.LogDebug(
                "A status could not connect over {AddressFamily}, so it goes over the other family. Error: {Error}",
                addressFamily, ex.Message);
            return null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _defaultInvoker.Dispose();
            _statusInvoker.Dispose();
        }

        base.Dispose(disposing);
    }
}
