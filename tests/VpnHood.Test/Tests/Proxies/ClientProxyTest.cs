using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Proxies.Management;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Core.Proxies.Management.Sqlite;
using VpnHood.Core.Server.Access;
using VpnHood.Net.Proxies.HttpProxyServers;
using VpnHood.Net.Proxies.Socks5ProxyServers;
using VpnHood.Test.Device;
using VpnHood.Test.Dom;
using VpnHood.Test.Providers;
using VpnHood.Test.Providers.Proxies;
using ClientState = VpnHood.Core.Client.Abstractions.ClientState;

namespace VpnHood.Test.Tests.Proxies;

[TestClass]
public class ClientProxyTest : ProxyTestBase
{
    // the client derives the same db path from its storage folder
    private string ClientStoreDbPath =>
        Path.Combine(TestHelper.WorkingPath, "ClientCore", "proxies", "proxies.db");

    [TestMethod]
    public async Task The_VPN_connection_goes_through_the_stream_its_proxy_connection_hands_out()
    {
        // the socket's own stream went around an HTTPS proxy's TLS session
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var serverTask = AcceptTls(listener, certificate);

        await using var connector = new StreamCountingProxyConnector();
        var vpnEndPoint = new VpnEndPoint((IPEndPoint)listener.LocalEndpoint, "localhost",
            CertificateHash: null, PathBase: null);

        using var factory = new TcpStreamConnectionFactory(new TestSocketFactory(), connector, vpnEndPoint,
            (_, _, _, _) => true, tcpPacketChannelKernelBufferSize: null);

        await using var connection = await factory.CreateConnection("proxy-stream-test", isTcpPacketChannel: false,
            onConnectAttempt: null, TestCt);

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.IsGreaterThan(0, connector.WrittenByteCount);
    }

    [TestMethod]
    public async Task A_session_runs_through_an_HTTPS_proxy()
    {
        // the tunnel went around the proxy's TLS session, so no HTTPS proxy carried one
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var httpsProxyServer = new HttpsProxyServer(new HttpProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            ServerCertificate = certificate
        });

        httpsProxyServer.Start();

        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var connector = new TestHttpsProxyConnector(httpsProxyServer.ListenerEndPoint);
        await using var client = await TestHelper.CreateClient(TestHelper.CreateClientOptions(token),
            vpnAdapter: new TestNullVpnAdapter(), proxyConnector: connector);

        Assert.AreEqual(ClientState.Connected, client.State);
        Assert.IsGreaterThan(0, connector.ConnectionCount);
    }

    [TestMethod]
    public async Task A_proxy_that_refuses_its_credentials_is_reported_as_the_proxys_failure()
    {
        // its refusal read as the VPN server's answer of 401, so the server was taken for reachable
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            Username = "user",
            Password = "secret"
        });

        socks5ProxyServer.Start();

        var proxyEndPoint = ToProxyEndPoint(socks5ProxyServer.ListenerEndPoint);
        proxyEndPoint.Username = "user";
        proxyEndPoint.Password = "wrong";
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        await Assert.ThrowsExactlyAsync<UnreachableProxyServerException>(() => TestHelper.CreateClient(
            TestHelper.CreateClientOptions(token), vpnAdapter: new TestNullVpnAdapter(),
            proxyOptions: new ProxyOptions { Mode = ProxyMode.Simple, ProxyEndPoint = proxyEndPoint }));
    }

    [TestMethod]
    public async Task A_server_that_is_down_behind_a_working_proxy_is_reported_as_the_servers_failure()
    {
        // with the check against the server, no proxy passed, and the error named the proxies
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        socks5ProxyServer.Start();

        // a key whose server is gone: the proxy answers that it refused
        Token token;
        await using (var server = await TestHelper.CreateServer())
            token = await TestHelper.CreateAccessToken(server);

        await Assert.ThrowsExactlyAsync<UnreachableServerException>(() => TestHelper.CreateClient(
            TestHelper.CreateClientOptions(token), vpnAdapter: new TestNullVpnAdapter(),
            proxyOptions: new ProxyOptions {
                Mode = ProxyMode.Simple, ProxyEndPoint = ToProxyEndPoint(socks5ProxyServer.ListenerEndPoint)
            }));
    }

    [TestMethod]
    public async Task A_session_through_a_proxy_takes_TCP_alone()
    {
        // UDP and QUIC went to the server's address straight, around the proxy
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        socks5ProxyServer.Start();

        var clientOptions = TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Udp);
        await using var clientServerDom = await ClientServerDom.Create(TestHelper, clientOptions,
            proxyOptions: new ProxyOptions {
                Mode = ProxyMode.Simple,
                ProxyEndPoint = ToProxyEndPoint(socks5ProxyServer.ListenerEndPoint)
            });

        var session = clientServerDom.Client.RequiredSession;
        Assert.IsNull(session.Config.HostUdpEndPoint);
        Assert.IsNull(session.Config.HostQuicEndPoint);
        CollectionAssert.AreEqual(new[] { ChannelProtocol.Tcp }, session.Info.ChannelProtocols.ToArray());
        Assert.AreEqual(ChannelProtocol.Tcp, session.Status.ChannelProtocol);
        await TestHelper.Test_Https();
    }

    // Each protocol on its own: with both in one list, the connect goes on at the first to pass, and the session's few
    // connections may take that one alone
    [TestMethod]
    [DataRow(ProxyProtocol.Socks5)]
    [DataRow(ProxyProtocol.Http)]
    public async Task Connect_via_proxy(ProxyProtocol protocol)
    {
        // a local proxy of the protocol
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        using var httpProxyServer = new HttpProxyServer(new HttpProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        IPEndPoint proxyEndPoint;
        if (protocol == ProxyProtocol.Socks5) {
            socks5ProxyServer.Start();
            proxyEndPoint = socks5ProxyServer.ListenerEndPoint;
        }
        else {
            httpProxyServer.Start();
            proxyEndPoint = httpProxyServer.ListenerEndPoint;
        }

        // seed the shared endpoint store the client will open
        using (var store = new ProxyEndPointStore(ClientStoreDbPath)) {
            await Upsert(store, [
                new ProxyEndPointRecord {
                    EndPoint = new ProxyEndPoint {
                        Protocol = protocol,
                        Host = proxyEndPoint.Address.ToString(),
                        Port = proxyEndPoint.Port
                    }
                }
            ]);
        }

        var clientOptions = TestHelper.CreateClientOptions();
        clientOptions.AllowChannelReuse = false; // make sure not reuse connections
        var socketFactory = new EndPointRecordingSocketFactory();
        var clientServerDom = await ClientServerDom.Create(TestHelper, clientOptions,
            proxyOptions: new ProxyOptions { Mode = ProxyMode.Managed }, clientSocketFactory: socketFactory);

        await TestHelper.Test_Https();

        // Connections through the proxy, which the server search's probe makes too, and none straight to the server,
        // the session's own included
        var mgr = clientServerDom.Client.ProxyConnector as ManagedProxyConnector;
        Assert.IsNotNull(mgr);
        Assert.IsGreaterThan(0, mgr.Status.SessionStatus.SucceededCount, "No connection went through the proxy.");
        AssertNoDialTo(clientServerDom.Token, socketFactory);
    }

    [TestMethod]
    public async Task Connect_via_single_proxy()
    {
        // create a local SOCKS5 proxy using Socks5ProxyServer
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        socks5ProxyServer.Start();

        var clientOptions = TestHelper.CreateClientOptions();
        clientOptions.AllowChannelReuse = false; // make sure not reuse connections
        var clientServerDom = await ClientServerDom.Create(TestHelper, clientOptions,
            proxyOptions: new ProxyOptions {
                Mode = ProxyMode.Simple,
                ProxyEndPoint = new ProxyEndPoint {
                    Protocol = ProxyProtocol.Socks5,
                    Host = socks5ProxyServer.ListenerEndPoint.Address.ToString(),
                    Port = socks5ProxyServer.ListenerEndPoint.Port
                }
            });

        await TestHelper.Test_Https();

        // the lightweight path must not create the shared endpoint db
        var connector = clientServerDom.Client.ProxyConnector as SimpleProxyConnector;
        Assert.IsNotNull(connector);
        Assert.IsFalse(File.Exists(ClientStoreDbPath));
        var status = connector.Status;
        Assert.IsTrue(status.SessionStatus.SucceededCount >= 1);
    }

    // The wait was the server search's own probe's, five seconds by default, so a list that came later never carried a
    // session, and the failed connect's close ended its download. The connect now waits for it under its own time: the
    // clock here starts once the client waits, at the proxies' step, or at the search's, where the old code waited
    [TestMethod]
    public async Task A_list_that_comes_after_the_servers_probe_time_still_carries_the_session()
    {
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        socks5ProxyServer.Start();
        using var listServer = new ListServer($"socks5://{socks5ProxyServer.ListenerEndPoint}", isHeld: true, TestCt);
        await using var server = await TestHelper.CreateServer();
        var clientOptions = TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server));
        clientOptions.Transport.ServerQueryTimeout = TimeSpan.FromSeconds(3);
        await using var client = await TestHelper.CreateClient(clientOptions, vpnAdapter: new TestNullVpnAdapter(),
            autoConnect: false, proxyOptions: CreateManagedListOptions(listServer.Url));

        var connecting = client.Connect(TestCt);
        await AssertEqualsWait(true, () => client.State is ClientState.ValidatingProxies or
            ClientState.FindingReachableServer, "The client did not come to its wait.");

        await AssertEqualsWait(1, () => listServer.DownloadCount, "The list's first download did not start.");
        await Task.Delay(TimeSpan.FromSeconds(4), TestCt);
        listServer.Release();
        await connecting;

        Assert.AreEqual(ClientState.Connected, client.State);
        Assert.IsNotNull(client.ProxyConnector);
        Assert.IsGreaterThan(0, client.ProxyConnector.Status.SessionStatus.SucceededCount,
            "The session went around the list's proxy.");
    }

    // A list that had only just come was left to the server search's first probe to check, within the probe's own few
    // seconds, which a slow proxy's check and then its connection took whole: the connect said that the server could
    // not be reached. It is now checked before the search, under the connect's own time, until a proxy passes; a
    // silent one beside it, whose check goes on behind the connect, is no reason for the search to check again
    [TestMethod]
    public async Task A_slow_proxy_of_a_list_that_has_only_just_come_still_carries_the_session()
    {
        using var proxy = new TestSocks5Proxy { ConnectDelay = TimeSpan.FromSeconds(3) }.Start();
        using var silentProxy = new TcpListener(IPAddress.Loopback, 0); // it takes a connection and answers nothing
        silentProxy.Start();
        using var listServer = new ListServer($"socks5://{proxy.EndPoint}\nsocks5://{silentProxy.LocalEndpoint}",
            isHeld: true, TestCt);

        await using var server = await TestHelper.CreateServer();
        var clientOptions = TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server));
        clientOptions.Transport.ServerQueryTimeout = TimeSpan.FromSeconds(6);
        clientOptions.Transport.TcpConnectTimeout = TimeSpan.FromSeconds(10); // the tests' 3 s is the proxy's own time
        await using var client = await TestHelper.CreateClient(clientOptions, vpnAdapter: new TestNullVpnAdapter(),
            autoConnect: false, proxyOptions: CreateManagedListOptions(listServer.Url));

        // the list comes once the client waits for it, so the client never finds one at its start
        var connecting = client.Connect(TestCt);
        await AssertEqualsWait(ClientState.ValidatingProxies, () => client.State);
        await AssertEqualsWait(1, () => listServer.DownloadCount, "The list's first download did not start.");
        listServer.Release();
        await connecting;

        Assert.AreEqual(ClientState.Connected, client.State);
        Assert.IsNotNull(client.ProxyConnector);
        Assert.IsGreaterThan(0, client.ProxyConnector.Status.SessionStatus.SucceededCount,
            "The session went around the list's proxy.");
    }

    // A list whose download brings no proxy: the connect says that no proxy can be reached, and dials nothing else
    [TestMethod]
    public async Task A_list_that_brings_no_proxy_ends_the_connect_with_no_proxy_reached()
    {
        using var listServer = new ListServer("no proxy here", isHeld: false, TestCt);
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var socketFactory = new EndPointRecordingSocketFactory();
        await Assert.ThrowsExactlyAsync<UnreachableProxyServerException>(() => TestHelper.CreateClient(
            TestHelper.CreateClientOptions(token), vpnAdapter: new TestNullVpnAdapter(),
            proxyOptions: CreateManagedListOptions(listServer.Url), socketFactory: socketFactory));

        Assert.AreEqual(1, listServer.DownloadCount);
        AssertNoDialTo(token, socketFactory);
    }

    // A key known by name alone, with a list that brings no proxy: the connect went on to look the server's name up,
    // and its failed lookup said that the server could not be found. It now says that no proxy can be reached
    [TestMethod]
    public async Task A_key_known_by_name_alone_with_no_proxy_to_use_ends_with_no_proxy_reached()
    {
        using var listServer = new ListServer("no proxy here", isHeld: false, TestCt);
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        token.ServerToken.HostName = "proxied-server.invalid";
        token.ServerToken.IsValidHostName = true;
        token.ServerToken.HostEndPoints = null;

        await Assert.ThrowsExactlyAsync<UnreachableProxyServerException>(() => TestHelper.CreateClient(
            TestHelper.CreateClientOptions(token), vpnAdapter: new TestNullVpnAdapter(),
            proxyOptions: CreateManagedListOptions(listServer.Url)));
    }

    // With the user's proxies on and none in the list, the VPN connected straight to the server; it now says that no
    // proxy can be reached, and dials nothing
    [TestMethod]
    public async Task With_proxies_on_and_none_in_the_list_the_VPN_never_goes_straight_to_the_server()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var socketFactory = new EndPointRecordingSocketFactory();
        await Assert.ThrowsExactlyAsync<UnreachableProxyServerException>(() => TestHelper.CreateClient(
            TestHelper.CreateClientOptions(token), vpnAdapter: new TestNullVpnAdapter(),
            proxyOptions: new ProxyOptions { Mode = ProxyMode.Managed }, socketFactory: socketFactory));

        AssertNoDialTo(token, socketFactory);
    }

    // no TCP connection made to any of the key's server endpoints, as a connection around the proxies would be
    private static void AssertNoDialTo(Token token, EndPointRecordingSocketFactory socketFactory)
    {
        var serverEndPoints = token.ServerToken.HostEndPoints ?? [];
        Assert.IsNotEmpty(serverEndPoints);
        Assert.IsFalse(socketFactory.EndPoints.Any(serverEndPoints.Contains), "The VPN went straight to its server.");
    }

    // the client's managed list, from the URL alone, with an hour between downloads
    private static ProxyOptions CreateManagedListOptions(Uri url)
    {
        return new ProxyOptions {
            Mode = ProxyMode.Managed,
            AutoUpdateOptions = new ProxyAutoUpdateOptions { Url = url, Interval = TimeSpan.FromHours(1) }
        };
    }
}
