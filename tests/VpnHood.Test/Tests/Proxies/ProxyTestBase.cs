using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Proxies.Management;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Core.Proxies.Management.Sqlite;
using VpnHood.Net.Proxies.Socks5ProxyServers;
using VpnHood.Test.Providers;
using VpnHood.Test.Providers.Proxies;

namespace VpnHood.Test.Tests.Proxies;

// What the proxy tests share: a store of endpoints, and connectors over it
public abstract class ProxyTestBase : TestBase
{
    // the store's upsert with its usual flags, under the test's token
    protected Task Upsert(IProxyEndPointStore store, IReadOnlyList<ProxyEndPointRecord> records,
        bool keepExistingStatus = true, bool keepExistingEnabled = false)
    {
        return store.Upsert(records, keepExistingStatus, keepExistingEnabled, TestCt);
    }

    protected async Task<ProxyEndPointStore> CreateStore(params ProxyEndPoint[] proxyEndPoints)
    {
        var store = new ProxyEndPointStore(Path.Combine(TestHelper.WorkingPath, "proxies", "proxies.db"));
        if (proxyEndPoints.Length > 0)
            await Upsert(store, proxyEndPoints.Select(x => new ProxyEndPointRecord { EndPoint = x }).ToArray());
        return store;
    }

    // a connection through the connector's one proxy, whose status says it worked last, so the connect checks no
    // server first
    protected async Task<ConnectionThroughProxy> ConnectThroughProxy(
        TimeSpan? serverCheckTimeout = null, Func<IProxyEndPointStore, IProxyEndPointStore>? wrapStore = null)
    {
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        socks5ProxyServer.Start();
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();

        var store = await CreateStore();
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = new ProxyEndPoint {
                    Protocol = ProxyProtocol.Socks5,
                    Host = socks5ProxyServer.ListenerEndPoint.Address.ToString(),
                    Port = socks5ProxyServer.ListenerEndPoint.Port
                },
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }
        ]);

        var connector = await ManagedProxyConnector.Create(new ProxyOptions(), wrapStore?.Invoke(store) ?? store,
            socketFactory: null, serverCheckTimeout, TestCt);

        var connection = await connector.ConnectAsync(new TestSocketFactory(),
            (IPEndPoint)target.LocalEndpoint, hostName: null, serverCertificateValidation: null, onAttempt: null,
            TestCt);

        return new ConnectionThroughProxy(connector, connection.TcpClient);
    }

    // an endpoint of the test SOCKS5 proxy
    protected static ProxyEndPoint ToProxyEndPoint(IPEndPoint endPoint, bool isEnabled = true)
    {
        return new ProxyEndPoint {
            Protocol = ProxyProtocol.Socks5,
            Host = endPoint.Address.ToString(),
            Port = endPoint.Port,
            IsEnabled = isEnabled
        };
    }

    // the name a TLS client gave at its handshake with the first connection the listener takes
    protected async Task<string> AcceptTls(TcpListener listener, X509Certificate2 certificate)
    {
        using var client = await listener.AcceptTcpClientAsync(TestCt);
        await using var sslStream = new SslStream(client.GetStream());
        await sslStream.AuthenticateAsServerAsync(
            new SslServerAuthenticationOptions { ServerCertificate = certificate }, TestCt);

        return sslStream.TargetHostName;
    }

    // checks with TLS; the test proxies are all on this machine's address, which the rule of one endpoint per address
    // would leave to one
    protected static ProxyOptions TlsCheckOptions => new() {
        VerifyTls = true,
        AutoUpdateOptions = new ProxyAutoUpdateOptions { RemoveDuplicateIps = false }
    };

    // A connector that checks with TLS, one handshake at a time beside the session, whose store has one proxy that
    // worked last: a connection through it gives the checks behind the session their server
    protected async Task<ManagedProxyConnector> CreateConnectorWithAWorkingProxy(TestSocks5Proxy proxy,
        TimeSpan serverCheckTimeout)
    {
        var store = await CreateStore();
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(proxy.EndPoint),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }
        ]);

        var connector = await ManagedProxyConnector.Create(TlsCheckOptions, store, socketFactory: null,
            serverCheckTimeout, TestCt);

        connector.HandshakePlaces = new SemaphoreSlim(1);
        connector.ConnectionHandshakePlaces = new SemaphoreSlim(1);
        return connector;
    }
}
