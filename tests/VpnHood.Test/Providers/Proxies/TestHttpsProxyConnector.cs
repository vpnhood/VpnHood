using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using VpnHood.Core.Proxies.Management;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Net.Toolkit.Monitoring;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Test.Providers.Proxies;

// a connector of one HTTPS proxy whose certificate it takes, as the connectors take a trusted one, and whose
// connection it hands out as theirs: through the factory's choice of its stream
internal sealed class TestHttpsProxyConnector(IPEndPoint proxyEndPoint) : IProxyConnector
{
    private int _connectionCount;
    public int ConnectionCount => Volatile.Read(ref _connectionCount);
    public bool IsEnabled => true;
    public bool UseRecentSucceeded { get; set; }
    public ProgressStatus? Progress => null;

    public ProxyConnectorStatus Status => new() {
        AutoUpdate = false, SessionStatus = new ProxySessionStatus(), IsAnySucceeded = true,
        SucceededServerCount = 1, FailedServerCount = 0, UnknownServerCount = 0, DisabledServerCount = 0
    };

    public async Task<ProxyConnection> ConnectAsync(ISocketFactory socketFactory, IPEndPoint ipEndPoint,
        string? hostName, RemoteCertificateValidationCallback? serverCertificateValidation, Action? onAttempt,
        CancellationToken cancellationToken)
    {
        var proxyClient = new VpnHood.Net.Proxies.HttpProxyClients.HttpProxyClient(
            new VpnHood.Net.Proxies.HttpProxyClients.HttpProxyClientOptions {
                ProxyEndPoint = proxyEndPoint,
                UseTls = true,
                AllowInvalidCertificates = true
            });

        var tcpClient = socketFactory.CreateTcpClient(proxyEndPoint);
        await proxyClient.ConnectAsync(tcpClient, ipEndPoint, cancellationToken);
        Interlocked.Increment(ref _connectionCount);
        return ProxyClientFactory.GetConnection(proxyClient, tcpClient);
    }

    public void RecordFailed(TcpClient tcpClient, Exception ex) { }

    public Task<bool> WaitForEndPoints(CancellationToken cancellationToken) => Task.FromResult(true);

    public Task CheckServers(ISocketFactory socketFactory, IPEndPoint ipEndPoint, string? hostName,
        RemoteCertificateValidationCallback? serverCertificateValidation, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task UpdateOptions(ProxyOptions proxyOptions, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Flush(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => default;
}
