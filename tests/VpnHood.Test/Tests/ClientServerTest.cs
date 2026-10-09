using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Test.AccessManagers;
using VpnHood.Test.Device;
using VpnHood.Test.Dom;
using VpnHood.Test.Extensions;
using VpnHood.Test.Providers;
using ClientState = VpnHood.Core.Client.Abstractions.ClientState;
// ReSharper disable ShortLivedHttpClient

// ReSharper disable DisposeOnUsingVariable

namespace VpnHood.Test.Tests;

[TestClass]
public class ClientServerTest : TestBase
{
    [TestMethod]
    [Obsolete]
    public async Task Redirect_Server_HostEndPoint()
    {
        // Create Server 1
        var fileAccessManagerOptions1 = TestHelper.CreateFileAccessManagerOptions();
        using var accessManager1 = TestHelper.CreateAccessManager(fileAccessManagerOptions1);
        await using var server1 = await TestHelper.CreateServer(accessManager1);

        // Create Server 2
        var serverEndPoint2 = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);
        var fileAccessManagerOptions2 = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions2.TcpEndPoints = [serverEndPoint2];
        using var accessManager2 =
            TestHelper.CreateAccessManager(fileAccessManagerOptions2, accessManager1.StoragePath);
        await using var server2 = await TestHelper.CreateServer(accessManager2);

        // redirect server1 to server2
        accessManager1.RedirectHostEndPoint = serverEndPoint2;

        // Create Client
        var token1 = TestHelper.CreateAccessToken(accessManager1);
        await using var client = await TestHelper.CreateClient(token1, vpnAdapter: TestHelper.CreateTestVpnAdapter());
        await TestHelper.Test_Https();

        Assert.AreEqual(serverEndPoint2, client.RequiredSession.Config.HostTcpEndPoint);
    }

    [TestMethod]
    public async Task Redirect_Server_ServerToken()
    {
        // Create Server 1
        var fileAccessManagerOptions1 = TestHelper.CreateFileAccessManagerOptions();
        using var accessManager1 = TestHelper.CreateAccessManager(fileAccessManagerOptions1);
        await using var server1 = await TestHelper.CreateServer(accessManager1);

        // Create Server 2
        var serverEndPoint2 = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);
        var fileAccessManagerOptions2 = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions2.TcpEndPoints = [serverEndPoint2];
        using var accessManager2 =
            TestHelper.CreateAccessManager(fileAccessManagerOptions2, accessManager1.StoragePath);
        await using var server2 = await TestHelper.CreateServer(accessManager2);
        var token = accessManager2.CreateToken();

        // redirect server1 to server2
        accessManager1.RedirectServerTokens = [token.ServerToken];

        // Create Client
        var token1 = TestHelper.CreateAccessToken(accessManager1);
        await using var client = await TestHelper.CreateClient(token1, vpnAdapter: TestHelper.CreateTestVpnAdapter());
        await TestHelper.Test_Https();

        Assert.AreEqual(serverEndPoint2, client.RequiredSession.Config.HostTcpEndPoint);
    }


    [TestMethod]
    public async Task Redirect_Server_By_ServerLocation()
    {
        // Create Server 1
        var fileAccessManagerOptions1 = TestHelper.CreateFileAccessManagerOptions();
        using var accessManager1 =
            TestHelper.CreateAccessManager(fileAccessManagerOptions1, serverLocation: "US/california");
        await using var server1 = await TestHelper.CreateServer(accessManager1);
        var token1 = accessManager1.CreateToken();

        // Create Server 2
        var fileAccessManagerOptions2 = TestHelper.CreateFileAccessManagerOptions();
        using var accessManager2 = TestHelper.CreateAccessManager(fileAccessManagerOptions2, accessManager1.StoragePath,
            serverLocation: "UK/london");
        await using var server2 = await TestHelper.CreateServer(accessManager2);
        var token2 = accessManager2.CreateToken();

        // redirect server1 to server2
        accessManager1.ServerLocations.Add("US/california", token1.ServerToken);
        accessManager1.ServerLocations.Add("UK/london", token2.ServerToken);

        // Create Client
        var token = TestHelper.CreateAccessToken(accessManager1);
        var clientOptions = TestHelper.CreateClientOptions(token: token);
        clientOptions.ServerLocation = "UK/london";
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions,
            vpnAdapter: new TestNullVpnAdapter());

        Assert.AreEqual(server2.ServerHost.TcpEndPoints.First(), client.RequiredSession.Config.HostTcpEndPoint);
        Assert.AreEqual("UK/london", client.RequiredSession.Info.ServerLocationInfo?.ServerLocation);
    }

    [TestMethod]
    public async Task Client_must_update_ServerLocation_from_access_manager()
    {
        // Create Server
        var fileAccessManagerOptions1 = TestHelper.CreateFileAccessManagerOptions();
        using var accessManager1 =
            TestHelper.CreateAccessManager(fileAccessManagerOptions1, serverLocation: "US/california");
        await using var server1 = await TestHelper.CreateServer(accessManager1);

        // create client
        var token1 = TestHelper.CreateAccessToken(accessManager1);
        await using var client = await TestHelper.CreateClient(token1, vpnAdapter: new TestNullVpnAdapter());
        Assert.AreEqual("US/california", client.RequiredSession.Info.ServerLocationInfo?.ServerLocation);
    }

    [TestMethod]
    public async Task UdpPackets_Drop()
    {
        // Create Server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.DropUdp = true;
        clientOptions.Transport.MaxPacketChannelCount = 6;
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions);
        await Assert.ThrowsAsync<OperationCanceledException>(() => TestHelper.Test_UdpEcho(timeout: TimeSpan.FromSeconds(3)),
            "UDP must be failed.");
    }


    [TestMethod]
    public async Task MaxPacketChannels()
    {
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.MaxPacketChannelCount = 3;

        // Create Server
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);
        var token = TestHelper.CreateAccessToken(server);

        // --------
        // Check: Client MaxPacketChannelCount larger than server
        // --------
        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.ChannelProtocol = ChannelProtocol.Tcp;
        clientOptions.Transport.MaxPacketChannelCount = 6;
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions,
            vpnAdapter: TestHelper.CreateTestVpnAdapter());

        // let channel be created gradually
        for (var i = 0; i < 6; i++) {
            await TestHelper.Test_UdpEcho();
            await Task.Delay(50, TestCt);
        }

        Thread.Sleep(100);
        Assert.AreEqual(3, client.RequiredSession.Status.ActivePacketChannelCount);
        await client.DisposeAsync();

        // --------
        // Check: Client MaxPacketChannelCount smaller than server
        // --------
        clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.ChannelProtocol = ChannelProtocol.Tcp;
        clientOptions.Transport.MaxPacketChannelCount = 1;
        await using var client2 = await TestHelper.CreateClient(clientOptions: clientOptions);

        // let channel be removed gradually
        for (var i = 0; i < 6; i++) {
            await TestHelper.Test_UdpEcho();
            await Task.Delay(50, TestCt);
        }

        Thread.Sleep(200);
        Assert.AreEqual(1, client2.RequiredSession.Status.ActivePacketChannelCount);
        await client.DisposeAsync();
    }

    [TestMethod]
    public async Task PacketChannel_Stream()
    {
        // Create Server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.Transport.MaxPacketChannelCount = 4;
        await using var client = await TestHelper.CreateClient(
            vpnAdapter: TestHelper.CreateTestVpnAdapter(), clientOptions: clientOptions);

        var tasks = new List<Task>();
        for (var i = 0; i < 50; i++)
            tasks.Add(TestHelper.Test_UdpEcho());

        await Task.WhenAll(tasks);
    }

    [TestMethod]
    public async Task PacketChannel_Udp()
    {
        VhLogger.MinLogLevel = LogLevel.Trace;

        // Create Server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        await using var client = await TestHelper.CreateClient(
            vpnAdapter: TestHelper.CreateTestVpnAdapter(),
            clientOptions: TestHelper.CreateClientOptions(token, channelProtocol: ChannelProtocol.Udp));

        var tasks = new List<Task>();
        for (var i = 0; i < 50; i++)
            tasks.Add(TestHelper.Test_UdpEcho());

        await Task.WhenAll(tasks);
    }


    [TestMethod]
    public async Task UdpChannel_custom_udp_port()
    {
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.UdpEndPoints = fileAccessManagerOptions.UdpEndPoints!
            .Select(x => VhUtils.GetFreeUdpEndPoint(x.Address))
            .ToArray();

        // Create Server
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        await using var client = await TestHelper.CreateClient(
            vpnAdapter: new TestNullVpnAdapter(),
            clientOptions: TestHelper.CreateClientOptions(token, channelProtocol: ChannelProtocol.Udp));

        Assert.IsTrue(fileAccessManagerOptions.UdpEndPoints.Any(x => x.Port == client.RequiredSession.Config.HostUdpEndPoint?.Port));
    }

    [TestMethod]
    public void Client_must_dispose_after_device_closed_foo()
    {
        Client_must_dispose_after_device_closed().GetAwaiter().GetResult();
    }


    [TestMethod]
    public async Task Client_must_dispose_after_device_closed()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        using var vpnAdapter = new TestNullVpnAdapter();
        await using var client = await TestHelper.CreateClient(token, vpnAdapter);

        Log("Disposing teh adapter...");
        vpnAdapter.Dispose();

        Log("Waiting for Disposed state...");
        await client.WaitForState(ClientState.Disposed);
    }

    [TestMethod]
    public async Task Client_must_dispose_after_server_stopped()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create client
        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.Transport.SessionTimeout = TimeSpan.FromSeconds(1);
        await using var client = await TestHelper.CreateClient(
            vpnAdapter: TestHelper.CreateTestVpnAdapter(), clientOptions: clientOptions);

        // success
        await TestHelper.Test_Https();

        // stop server
        Log("Disposing the server...");
        await server.DisposeAsync();

        // failed
        Log("Waiting for client to be disposed.");
        await Assert.ThrowsAsync<Exception>(() => TestHelper.Test_Https());
        await Task.Delay(1000); // wait to finish session time after first error
        await Assert.ThrowsAsync<Exception>(() => TestHelper.Test_Https());

        await client.WaitForState(ClientState.Disposed);
    }

    [TestMethod]
    public async Task Client_is_connecting_until_its_adapter_has_started()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // what the client says of itself while its adapter is still starting; by then the server has
        // answered its first requests
        using var vpnAdapter = new StartingVpnAdapter();
        await using var client = await TestHelper.CreateClient(token, vpnAdapter, autoConnect: false);
        ClientState? stateWhileStarting = null;
        vpnAdapter.Starting += (_, _) => stateWhileStarting = client.State;

        await client.Connect(TestCt);
        Assert.AreEqual(ClientState.Connecting, stateWhileStarting);
        Assert.AreEqual(ClientState.Connected, client.State);
    }

    [TestMethod]
    public async Task Request_answered_while_disconnecting_does_not_reconnect()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token, vpnAdapter: new TestNullVpnAdapter());
        var session = (VpnHood.Core.Client.ClientSession)client.RequiredSession;

        // the server answers a request right after the session starts closing, before its bye goes
        var states = new List<ClientState>();
        var isAnswered = false;
        session.StateChanged += (_, _) => {
            lock (states) {
                states.Add(session.State);
                if (states.Count > 1)
                    return;
            }

            isAnswered = session.UpdateStatus(TestCt).Wait(TimeSpan.FromSeconds(10));
        };

        await client.DisposeAsync();
        Assert.IsTrue(isAnswered);
        CollectionAssert.AreEqual(new[] { ClientState.Disconnecting, ClientState.Disposed }, states);
    }

    [TestMethod]
    public async Task Client_disposed_while_waiting_for_ad_does_not_start_its_adapter()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var accessToken = accessManager.AccessTokenService.Create(adRequirement: AdRequirement.Flexible);
        var token = accessManager.GetToken(accessToken);

        // the client's start waits for an ad that nobody shows
        using var vpnAdapter = new StartingVpnAdapter();
        var isAdapterStarted = false;
        vpnAdapter.Starting += (_, _) => isAdapterStarted = true;
        await using var client = await TestHelper.CreateClient(token, vpnAdapter, autoConnect: false);
        var connectTask = client.Connect(TestCt);
        await client.WaitForState(ClientState.WaitingForAd);

        // the user disconnects meanwhile: the start ends there, and no error is left to show
        await client.DisposeAsync();
        Assert.IsFalse(isAdapterStarted);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
            connectTask.WaitAsync(TimeSpan.FromSeconds(10), TestCt));
        Assert.AreEqual(ClientState.Disposed, client.State);
        Assert.IsNull(client.LastException);
    }

    [TestMethod]
    public async Task Session_closed_by_server_while_waiting_for_ad_fails_the_start_with_its_error()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var accessToken = accessManager.AccessTokenService.Create(adRequirement: AdRequirement.Flexible);
        var token = accessManager.GetToken(accessToken);

        // the client's start waits for an ad that nobody shows
        using var vpnAdapter = new StartingVpnAdapter();
        var isAdapterStarted = false;
        vpnAdapter.Starting += (_, _) => isAdapterStarted = true;
        await using var client = await TestHelper.CreateClient(token, vpnAdapter, autoConnect: false);
        var connectTask = client.Connect(TestCt);
        await client.WaitForState(ClientState.WaitingForAd);

        // the server closes the session meanwhile, and the client's next request finds out
        await server.SessionManager.CloseSession(client.SessionId, TestCt);
        await Assert.ThrowsExactlyAsync<SessionException>(() => client.UpdateSessionStatus(TestCt));

        // the start fails with what closed the session, not with a disposed session
        var ex = await Assert.ThrowsExactlyAsync<SessionException>(() =>
            connectTask.WaitAsync(TimeSpan.FromSeconds(10), TestCt));
        Assert.AreEqual(SessionErrorCode.SessionClosed, ex.SessionResponse.ErrorCode);
        Assert.AreSame(ex, client.LastException);
        Assert.IsFalse(isAdapterStarted);
    }

    [TestMethod]
    public async Task PacketChannel_after_client_reconnection()
    {
        //create a shared udp client among connection
        // make sure using same local port to test Nat properly
        using var udpClient = new UdpClient();
        using var ping = new Ping();

        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);

        // create client
        await using (await TestHelper.CreateClient(token, vpnAdapter: TestHelper.CreateTestVpnAdapter())) {
            // test Icmp & Udp
            await TestHelper.Test_Ping(ping);
            await TestHelper.Test_UdpEcho(udpClient);
        }

        // create client
        await using (await TestHelper.CreateClient(token, vpnAdapter: TestHelper.CreateTestVpnAdapter())) {
            // test Icmp & Udp
            await TestHelper.Test_Ping(ping);
            await TestHelper.Test_UdpEcho(udpClient);
        }
    }

    [TestMethod]
    public async Task Reset_tcp_connection_immediately_after_vpn_connected()
    {
        VhLogger.MinLogLevel = LogLevel.Trace;

        // a fixed anycast endpoint that always accepts tcp on 443 — a literal ip, so this test never
        // depends on a DNS lookup that a concurrently running tunnel test may capture
        var httpsExternalEndPoint = IPEndPoint.Parse("1.1.1.1:443");

        // connect to a host
        using TcpClient tcpClient = new();
        using var connectCts = new CancellationTokenSource(2000);
        await tcpClient.ConnectAsync(httpsExternalEndPoint, connectCts.Token);
        await using var stream = tcpClient.GetStream();

        // make sure the client routes the external host through the vpn by adding the host ip to allowed list
        var clientOptions = TestHelper.CreateClientOptions();
        clientOptions.IncludeIpRangesByDevice = clientOptions.IncludeIpRangesByDevice
            .Union(new[] { httpsExternalEndPoint.Address }.ToIpRanges())
            .ToArray();
        await using var dom = await ClientServerDom.Create(TestHelper, clientOptions: clientOptions);

        using var cts2 = new CancellationTokenSource(2000);
        var ex = await Assert.ThrowsAsync<Exception>(async () => {
            await stream.WriteAsync(new byte[1], cts2.Token);
            // ReSharper disable once MustUseReturnValue
            await stream.ReadAsync(new byte[1], cts2.Token);
        });

        var innerException = ex.InnerException;
        Assert.AreEqual(typeof(SocketException), innerException?.GetType());
        Assert.AreEqual(SocketError.ConnectionReset, ((SocketException)innerException!).SocketErrorCode);
    }

    [TestMethod]
    public async Task Disconnect_if_session_closed_by_server()
    {
        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);

        // connect
        await using var client = await TestHelper.CreateClient(token, vpnAdapter: TestHelper.CreateTestVpnAdapter());
        Assert.AreEqual(ClientState.Connected, client.State);

        // close session
        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Closing the session by Test.");
        await server.SessionManager.CloseSession(client.SessionId, TestCt);

        // wait for disposing session in access server
        await AssertEqualsWait(false, () =>
                accessManager.SessionService.Sessions.TryGetValue(client.SessionId, out var session) &&
                session.IsAlive,
            "Session has not been closed in the access server.");

        try {
            await TestHelper.Test_Https();
        }
        catch {
            // ignored
        }

        await client.WaitForState(ClientState.Disposed);
    }

    [TestMethod]
    public async Task Configure_Maintenance_Server()
    {
        // --------
        // Check: AccessManager is on at start
        // --------
        var accessManager = TestHttpAccessManager.Create(TestHelper.CreateAccessManager());
        await using var server = await TestHelper.CreateServer(accessManager);

        Assert.IsFalse(server.AccessManager.IsMaintenanceMode);
        await server.DisposeAsync();

        // ------------
        // Check: AccessManager is off at start
        // ------------
        accessManager.HttpAccessManagerServer.Stop();
        await using var server2 = await TestHelper.CreateServer(accessManager, false);
        await server2.Start(TestCt);

        // ----------
        // Check: MaintenanceMode is expected
        // ----------
        var token = TestHelper.CreateAccessToken(server);

        // while the access manager is unreachable, detecting maintenance costs the server a refused
        // round trip, which takes seconds on a loaded machine; the default 3s connect budget is too tight
        Task<Core.Client.VpnHoodClient> CreateClientAsync(bool autoConnect)
        {
            var clientOptions = TestHelper.CreateClientOptions(token);
            clientOptions.Transport.TcpConnectTimeout = TimeSpan.FromSeconds(30);
            return TestHelper.CreateClient(clientOptions, new TestNullVpnAdapter(), autoConnect);
        }

        await using var client = await CreateClientAsync(autoConnect: false);
        var ex = await Assert.ThrowsExactlyAsync<MaintenanceException>(() => client.Connect(cancellationToken: TestCt));

        Assert.AreEqual(SessionErrorCode.Maintenance, ex.SessionResponse.ErrorCode);
        Assert.AreEqual(ClientState.Disposed, client.State);

        // ----------
        // Check: Connect after Maintenance is done
        // ----------
        accessManager.HttpAccessManagerServer.Start();
        await using var client2 = await CreateClientAsync(autoConnect: true);
        await client2.WaitForState(ClientState.Connected);

        // ----------
        // Check: Go Maintenance mode after server started by replying 503 from access-server
        // ----------
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = HttpStatusCode.ServiceUnavailable;
        await using var client3 = await CreateClientAsync(autoConnect: false);
        ex = await Assert.ThrowsExactlyAsync<MaintenanceException>(() => client3.Connect(cancellationToken: TestCt));
        Assert.AreEqual(SessionErrorCode.Maintenance, ex.SessionResponse.ErrorCode);

        // ----------
        // Check: Connect after Maintenance is done
        // ----------
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = null;
        await using var client4 = await CreateClientAsync(autoConnect: true);
        await client4.WaitForState(ClientState.Connected);

        // ----------
        // Check: Go Maintenance mode by replying 403 from access-server
        // ----------
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = HttpStatusCode.Forbidden;
        await using var client5 = await CreateClientAsync(autoConnect: false);
        ex = await Assert.ThrowsExactlyAsync<MaintenanceException>(() => client5.Connect(cancellationToken: TestCt));
        Assert.AreEqual(SessionErrorCode.Maintenance, ex.SessionResponse.ErrorCode);

        // ----------
        // Check: Connect after Maintenance is done
        // ----------
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = null;
        await using var client6 = await CreateClientAsync(autoConnect: true);
        await client6.WaitForState(ClientState.Connected);
    }

    [TestMethod]
    public async Task Disconnect_if_client_not_supported()
    {
        // create server
        await using var server = await TestHelper.CreateServer();
        server.ServerHost.MinClientProtocolVersion = 1000;

        var token = TestHelper.CreateAccessToken(server);

        // create client
        await using var client = await TestHelper.CreateClient(token, autoConnect: false);

        var ex = await Assert.ThrowsExactlyAsync<SessionException>(() => client.Connect());
        Assert.AreEqual(SessionErrorCode.UnsupportedClient, ex.SessionResponse.ErrorCode);
    }

    [TestMethod]
    public async Task Server_limit_by_Max_TcpConnectWait()
    {
        // create vpn adapter
        var vpnAdapter = TestHelper.CreateTestVpnAdapter();
        var socketFactory = TestHelper.CreateTestSocketFactory(vpnAdapter);

        // create access server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.MaxTcpConnectWaitCount = 2;
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions, socketFactory: socketFactory);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token, vpnAdapter: vpnAdapter);


        using var httpClient = new HttpClient();
        _ = httpClient.GetStringAsync($"https://{MockEps.HttpV4EndPointInvalid.Address}:4441", TestCt);
        _ = httpClient.GetStringAsync($"https://{MockEps.HttpV4EndPointInvalid.Address}:4442", TestCt);
        _ = httpClient.GetStringAsync($"https://{MockEps.HttpV4EndPointInvalid.Address}:4443", TestCt);
        _ = httpClient.GetStringAsync($"https://{MockEps.HttpV4EndPointInvalid.Address}:4445", TestCt);

        await Task.Delay(500, TestCt);
        var session = server.SessionManager.GetSessionById(client.SessionId);
        Assert.AreEqual(fileAccessManagerOptions.SessionOptions.MaxTcpConnectWaitCount, session?.TcpConnectWaitCount);
    }

    [TestMethod]
    public async Task Server_limit_by_Max_TcpChannel()
    {
        // create access server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.MaxTcpChannelCount = 2;
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);

        // create client
        var token = TestHelper.CreateAccessToken(server);

        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.ChannelProtocol = ChannelProtocol.Udp;
        clientOptions.UseTcpProxy = true;
        await using var client = await TestHelper.CreateClient(clientOptions);

        using var tcpClient1 = new TcpClient();
        using var tcpClient2 = new TcpClient();
        using var tcpClient3 = new TcpClient();
        using var tcpClient4 = new TcpClient();

        await Task.WhenAll(
            tcpClient1.ConnectAsync(MockEps.HttpV4EndPoint1, TestCt).AsTask(),
            tcpClient2.ConnectAsync(MockEps.HttpV4EndPoint1, TestCt).AsTask(),
            tcpClient3.ConnectAsync(MockEps.HttpV4EndPoint2, TestCt).AsTask(),
            tcpClient4.ConnectAsync(MockEps.HttpV4EndPoint2, TestCt).AsTask());

        tcpClient1.GetStream().WriteByte((byte)'G');
        tcpClient2.GetStream().WriteByte((byte)'G');
        tcpClient3.GetStream().WriteByte((byte)'G');
        tcpClient4.GetStream().WriteByte((byte)'G');

        var session = server.SessionManager.GetSessionById(client.SessionId);
        await AssertEqualsWait(fileAccessManagerOptions.SessionOptions.MaxTcpChannelCount, () => session?.TcpChannelCount);
    }

    [TestMethod]
    [DoNotParallelize] // asserts exact connection-reuse counts; under CPU contention a parked
    // shared connection can die from a canceled graceful close and the client legitimately
    // falls back to a new connection, breaking the exact counts
    public async Task Reusing_ChunkStream()
    {
        // Create Server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        await using var client =
            await TestHelper.CreateClient(
                clientOptions: TestHelper.CreateClientOptions(token, channelProtocol: ChannelProtocol.Udp));
        Assert.AreEqual(1, client.RequiredSession.Status.ConnectorStatus.CreatedConnectionCount);
        Assert.AreEqual(0, client.RequiredSession.Status.ConnectorStatus.ReusedConnectionSucceededCount);
        var lastCreatedConnectionCount = client.RequiredSession.Status.ConnectorStatus.CreatedConnectionCount;
        var lastReusedConnectionSucceededCount =
            client.RequiredSession.Status.ConnectorStatus.ReusedConnectionSucceededCount;

        // create one connection
        await Task.Delay(500, TestCt); // wait for connection to get ready
        Log("Test: Check the first HTTPS connection.");
        await TestHelper.Test_Https();
        Assert.AreEqual(lastReusedConnectionSucceededCount,
            client.RequiredSession.Status.ConnectorStatus.ReusedConnectionSucceededCount);
        Assert.AreEqual(lastCreatedConnectionCount + 1,
            client.RequiredSession.Status.ConnectorStatus.CreatedConnectionCount);
        lastCreatedConnectionCount = client.RequiredSession.Status.ConnectorStatus.CreatedConnectionCount;
        lastReusedConnectionSucceededCount = client.RequiredSession.Status.ConnectorStatus.ReusedConnectionSucceededCount;
        await AssertEqualsWait(1, () => client.RequiredSession.Status.ConnectorStatus.FreeConnectionCount);

        // this connection must reuse the old one
        await TestHelper.Test_Https();
        Assert.AreEqual(lastCreatedConnectionCount, client.RequiredSession.Status.ConnectorStatus.CreatedConnectionCount);
        Assert.AreEqual(lastReusedConnectionSucceededCount + 1,
            client.RequiredSession.Status.ConnectorStatus.ReusedConnectionSucceededCount);
        lastCreatedConnectionCount = client.RequiredSession.Status.ConnectorStatus.CreatedConnectionCount;
        lastReusedConnectionSucceededCount = client.RequiredSession.Status.ConnectorStatus.ReusedConnectionSucceededCount;
        await AssertEqualsWait(1, () => client.RequiredSession.Status.ConnectorStatus.FreeConnectionCount);

        // this connection must reuse the old one again
        await TestHelper.Test_Https();
        Assert.AreEqual(lastCreatedConnectionCount, client.RequiredSession.Status.ConnectorStatus.CreatedConnectionCount);
        Assert.AreEqual(lastReusedConnectionSucceededCount + 1,
            client.RequiredSession.Status.ConnectorStatus.ReusedConnectionSucceededCount);
        lastCreatedConnectionCount = client.RequiredSession.Status.ConnectorStatus.CreatedConnectionCount;
        lastReusedConnectionSucceededCount = client.RequiredSession.Status.ConnectorStatus.ReusedConnectionSucceededCount;
        await AssertEqualsWait(1, () => client.RequiredSession.Status.ConnectorStatus.FreeConnectionCount);

        // open 3 connections simultaneously
        VhLogger.Instance.LogDebug("Test: Open 3 connections simultaneously.");
        using (var tcpClient1 = new TcpClient())
        using (var tcpClient2 = new TcpClient())
        using (var tcpClient3 = new TcpClient()) {
            await tcpClient1.ConnectAsync(MockEps.HttpV4EndPoint1, TestCt);
            await tcpClient2.ConnectAsync(MockEps.HttpV4EndPoint1, TestCt);
            await tcpClient3.ConnectAsync(MockEps.HttpV4EndPoint1, TestCt);
            tcpClient1.GetStream().WriteByte(1);
            tcpClient2.GetStream().WriteByte(1);
            tcpClient3.GetStream().WriteByte(1);

            await AssertEqualsWait(lastCreatedConnectionCount + 2,
                () => client.RequiredSession.Status.ConnectorStatus.CreatedConnectionCount);
            await AssertEqualsWait(lastReusedConnectionSucceededCount + 1,
                () => client.RequiredSession.Status.ConnectorStatus.ReusedConnectionSucceededCount);
            lastCreatedConnectionCount = client.RequiredSession.Status.ConnectorStatus.CreatedConnectionCount;
            lastReusedConnectionSucceededCount =
                client.RequiredSession.Status.ConnectorStatus.ReusedConnectionSucceededCount;
        }

        Log("Test: Waiting for free connections...");
        await AssertEqualsWait(3, () => client.RequiredSession.Status.ConnectorStatus.FreeConnectionCount);

        // net two connection should use shared connection
        using (var tcpClient4 = new TcpClient())
        using (var tcpClient5 = new TcpClient()) {
            await tcpClient4.ConnectAsync(MockEps.HttpsV4EndPoint1);
            await tcpClient5.ConnectAsync(MockEps.HttpsV4EndPoint2);
            tcpClient4.GetStream().WriteByte(1);
            tcpClient5.GetStream().WriteByte(1);
            await AssertEqualsWait(lastCreatedConnectionCount,
                () => client.RequiredSession.Status.ConnectorStatus.CreatedConnectionCount);
            await AssertEqualsWait(lastReusedConnectionSucceededCount + 2,
                () => client.RequiredSession.Status.ConnectorStatus.ReusedConnectionSucceededCount);
        }

        // wait for free the used connections 
        await AssertEqualsWait(3, () => client.RequiredSession.Status.ConnectorStatus.FreeConnectionCount);
    }

    [TestMethod]
    public async Task IsUdpChannelSupported_must_be_false_when_server_return_udp_port_zero()
    {
        // Create Server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.UdpEndPoints = [];
        await using var server = await TestHelper.CreateServer(options: fileAccessManagerOptions);
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        await using var client = await TestHelper.CreateClient(
            vpnAdapter: new TestNullVpnAdapter(),
            clientOptions: TestHelper.CreateClientOptions(token: token, channelProtocol: ChannelProtocol.Udp));

        Assert.IsFalse(client.RequiredSession.Info.IsUdpChannelSupported);
    }

    [TestMethod]
    public async Task ServerVpnAdapter_by_udp()
    {
        using var vpnAdapter = new TestUdpServerVpnAdapter();

        // check will server use the adapter 
        var adapterUsed = false;
        vpnAdapter.PacketReceived += (_, _) => { adapterUsed = true; };

        // create access server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions, vpnAdapter: vpnAdapter);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token);

        // test udp
        await TestHelper.Test_UdpEcho(MockEps.UdpV4EndPoint1);
        await TestHelper.Test_UdpEcho(MockEps.UdpV4EndPoint2);
        Assert.IsTrue(adapterUsed);
    }

    [TestMethod]
    public async Task ServerVpnAdapter_without_IpV6_NAT_takes_TCP_packets_of_IpV4_only()
    {
        // a server adapter whose NAT translates IPv4 only, as WinNAT does
        using var vpnAdapter = new TestUdpServerVpnAdapter { IsNatIpV6Supported = false };
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions, vpnAdapter: vpnAdapter);

        // the client sends IPv4 TCP as packets, and IPv6 TCP by its proxy
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(TestHelper.CreateClientOptions(token, useTcpProxy: false));
        Assert.IsTrue(client.RequiredSession.Info.IsTcpPacketIpV4Supported);
        Assert.IsFalse(client.RequiredSession.Info.IsTcpPacketIpV6Supported);
        Assert.IsFalse(client.RequiredSession.Status.IsTcpProxy);
    }

    [TestMethod]
    public async Task Set_DnsServer_to_vpnAdapter()
    {
        // Create Server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create app
        await using var client = await TestHelper.CreateClient(token, vpnAdapter: new TestNullVpnAdapter());
        await client.WaitForState(ClientState.Connected);

        Assert.IsNotEmpty(client.RequiredSession.Info.DnsConfig.DnsServers);
    }

    // a null adapter that tells when its start is under way
    private sealed class StartingVpnAdapter : TestNullVpnAdapter
    {
        public event EventHandler? Starting;

        protected override Task AdapterAdd(CancellationToken cancellationToken)
        {
            Starting?.Invoke(this, EventArgs.Empty);
            return base.AdapterAdd(cancellationToken);
        }
    }
}