using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using EmbedIO;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Client;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Server;
using VpnHood.Core.Tunneling;
using VpnHood.Test.AccessManagers;
using VpnHood.Test.Device;
using VpnHood.Test.Providers;
using ClientState = VpnHood.Core.Client.Abstractions.ClientState;

// ReSharper disable DisposeOnUsingVariable

namespace VpnHood.Test.Tests;

[TestClass]
public class ClientServerTest : TestBase
{
    [TestMethod]
    public async Task Redirect_Server()
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

        Assert.AreEqual(serverEndPoint2, client.HostTcpEndPoint);
    }

    [TestMethod]
    public async Task Redirect_Server_By_ServerLocation()
    {
        // Create Server 1
        var serverEndPoint1 = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);
        var fileAccessManagerOptions1 = TestHelper.CreateFileAccessManagerOptions(tcpEndPoints: [serverEndPoint1]);
        using var accessManager1 =
            TestHelper.CreateAccessManager(fileAccessManagerOptions1, serverLocation: "US/california");
        await using var server1 = await TestHelper.CreateServer(accessManager1);

        // Create Server 2
        var serverEndPoint2 = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);
        var fileAccessManagerOptions2 = TestHelper.CreateFileAccessManagerOptions(tcpEndPoints: [serverEndPoint2]);
        using var accessManager2 = TestHelper.CreateAccessManager(fileAccessManagerOptions2, accessManager1.StoragePath,
            serverLocation: "UK/london");
        await using var server2 = await TestHelper.CreateServer(accessManager2);

        // redirect server1 to server2
        accessManager1.ServerLocations.Add("US/california", serverEndPoint1);
        accessManager1.ServerLocations.Add("UK/london", serverEndPoint2);

        // Create Client
        var token1 = TestHelper.CreateAccessToken(accessManager1);
        var clientOptions = TestHelper.CreateClientOptions(token: token1);
        clientOptions.ServerLocation = "UK/london";
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions,
            vpnAdapter: new TestNullVpnAdapter());

        Assert.AreEqual(serverEndPoint2, client.HostTcpEndPoint);
        Assert.AreEqual("UK/london", client.SessionInfo?.ServerLocationInfo?.ServerLocation);
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
        Assert.AreEqual("US/california", client.SessionInfo?.ServerLocationInfo?.ServerLocation);
    }

    [TestMethod]
    public async Task TcpChannel()
    {
        // Create Server
        var serverEp = VhUtils.GetFreeTcpEndPoint(IPAddress.IPv6Loopback);
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.TcpEndPoints = [serverEp];
        fileAccessManagerOptions.PublicEndPoints = [serverEp];

        using var accessManager = TestHelper.CreateAccessManager(fileAccessManagerOptions);
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.UseUdpChannel = false;
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions);

        await TestTunnel(server, client);

        // check HostEndPoint in server
        accessManager.SessionService.Sessions.TryGetValue(client.SessionId, out var session);
        Assert.IsTrue(token.ServerToken.HostEndPoints?.Any(x => x.Equals(session?.HostEndPoint)));

        // check UserAgent in server
        Assert.AreEqual(client.UserAgent, session?.ClientInfo.UserAgent);

        // check ClientPublicAddress in server
        Assert.AreEqual(serverEp.Address, client.SessionInfo?.ClientPublicIpAddress);
    }

    [TestMethod]
    public async Task UdpPackets_Drop()
    {
        // Create Server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.DropUdp = true;
        clientOptions.MaxDatagramChannelCount = 6;
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions);

        try {
            await TestHelper.Test_Udp(3000);
            Assert.Fail("UDP must be failed.");
        }
        catch (Exception ex) {
            Assert.AreEqual(nameof(OperationCanceledException), ex.GetType().Name);
        }
    }


    [TestMethod]
    public async Task MaxDatagramChannels()
    {
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.MaxDatagramChannelCount = 3;

        // Create Server
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);
        var token = TestHelper.CreateAccessToken(server);

        // --------
        // Check: Client MaxDatagramChannelCount larger than server
        // --------
        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.UseUdpChannel = false;
        clientOptions.MaxDatagramChannelCount = 6;
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions,
            vpnAdapter: TestHelper.CreateTestVpnAdapter());

        // let channel be created gradually
        for (var i = 0; i < 6; i++) {
            await TestHelper.Test_Udp(TestConstants.UdpV4EndPoint1);
            await Task.Delay(50);
        }

        Thread.Sleep(100);
        Assert.AreEqual(3, client.GetSessionStatus().DatagramChannelCount);
        await client.DisposeAsync();

        // --------
        // Check: Client MaxDatagramChannelCount smaller than server
        // --------
        clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.UseUdpChannel = false;
        clientOptions.MaxDatagramChannelCount = 1;
        await using var client2 = await TestHelper.CreateClient(clientOptions: clientOptions);

        // let channel be removed gradually
        for (var i = 0; i < 6; i++) {
            await TestHelper.Test_Udp(TestConstants.UdpV4EndPoint1);
            await Task.Delay(50);
        }

        Thread.Sleep(200);
        Assert.AreEqual(1, client2.GetSessionStatus().DatagramChannelCount);
        await client.DisposeAsync();
    }

    [TestMethod]
    public async Task DatagramChannel_Stream()
    {
        // Create Server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.MaxDatagramChannelCount = 4;
        await using var client = await TestHelper.CreateClient(
            vpnAdapter: TestHelper.CreateTestVpnAdapter(), clientOptions: clientOptions);

        var tasks = new List<Task>();
        for (var i = 0; i < 50; i++)
            tasks.Add(TestHelper.Test_Udp());

        await Task.WhenAll(tasks);
    }

    [TestMethod]
    public async Task DatagramChannel_Udp()
    {
        VhLogger.IsDiagnoseMode = true;

        // Create Server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        await using var client = await TestHelper.CreateClient(
            vpnAdapter: TestHelper.CreateTestVpnAdapter(),
            clientOptions: TestHelper.CreateClientOptions(token, useUdpChannel: true));

        var tasks = new List<Task>();
        for (var i = 0; i < 50; i++)
            tasks.Add(TestHelper.Test_Udp());

        await Task.WhenAll(tasks);
    }


    [TestMethod]
    public async Task UdpChannel()
    {
        VhLogger.IsDiagnoseMode = true;

        // Create Server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        await using var client =
            await TestHelper.CreateClient(clientOptions: TestHelper.CreateClientOptions(token, useUdpChannel: true));
        VhLogger.Instance.LogTrace(GeneralEventId.Test, "Test: Testing by UdpChannel.");
        Assert.IsTrue(client.UseUdpChannel);
        await TestTunnel(server, client);

        // switch to tcp
        VhLogger.Instance.LogTrace(GeneralEventId.Test, "Test: Switch to DatagramChannel.");
        client.UseUdpChannel = false;
        await TestTunnel(server, client);
        await VhTestUtil.AssertEqualsWait(false, () => client.GetSessionStatus().IsUdpMode);
        Assert.IsFalse(client.UseUdpChannel);

        // switch back to udp
        VhLogger.Instance.LogTrace(GeneralEventId.Test, "Test: Switch back to UdpChannel.");
        client.UseUdpChannel = true;
        await TestTunnel(server, client);
        await VhTestUtil.AssertEqualsWait(true, () => client.GetSessionStatus().IsUdpMode);
        Assert.IsTrue(client.UseUdpChannel);
    }

    [TestMethod]
    public async Task UdpChannel_custom_udp_port()
    {
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.UdpEndPoints = fileAccessManagerOptions.UdpEndPoints!
            .Select(x => VhUtils.GetFreeUdpEndPoint(x.Address)).ToArray();

        // Create Server
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        await using var client = await TestHelper.CreateClient(
            vpnAdapter: new TestNullVpnAdapter(),
            clientOptions: TestHelper.CreateClientOptions(token, useUdpChannel: true));

        Assert.IsTrue(fileAccessManagerOptions.UdpEndPoints.Any(x => x.Port == client.HostUdpEndPoint?.Port));
    }

    private async Task TestTunnel(VpnHoodServer server, VpnHoodClient client)
    {
        Assert.AreEqual(ServerState.Ready, server.State);
        Assert.AreEqual(ClientState.Connected, client.State);

        // Get session
        server.SessionManager.Sessions.TryGetValue(client.SessionId, out var serverSession);
        Assert.IsNotNull(serverSession, "Could not find session in server!");

        // ************
        // *** TEST ***: TCP invalid request should not close the vpn connection
        var oldClientSentByteCount = client.GetSessionStatus().SessionTraffic.Sent;
        var oldClientReceivedByteCount = client.GetSessionStatus().SessionTraffic.Received;
        var oldServerSentByteCount = serverSession.Tunnel.Traffic.Sent;
        var oldServerReceivedByteCount = serverSession.Tunnel.Traffic.Received;

        using var httpClient = new HttpClient();
        try {
            await httpClient.GetStringAsync($"http://{TestConstants.NsEndPoint1}",
                new CancellationTokenSource(2000).Token);
            Assert.Fail("Exception expected!");
        }
        catch {
            // ignored
        }

        Assert.AreEqual(ClientState.Connected, client.State);

        // ************
        // *** TEST ***: TCP (TLS) by quad9
        await TestHelper.Test_Https();

        // check some data has been sent
        Assert.AreNotEqual(oldClientSentByteCount, client.GetSessionStatus().SessionTraffic.Sent, delta: 100,
            "Not enough data has been sent through the client.");
        Assert.AreNotEqual(oldClientReceivedByteCount, client.GetSessionStatus().SessionTraffic.Received, delta: 2000,
            "Not enough data has been received through the client.");
        Assert.AreNotEqual(oldServerSentByteCount, serverSession.Tunnel.Traffic.Sent, delta: 2000,
            "Not enough data has been sent through the server.");
        Assert.AreNotEqual(oldServerReceivedByteCount, serverSession.Tunnel.Traffic.Received, delta: 100,
            "Not enough data has been received through the server.");

        // ************
        // *** TEST ***: UDP v4
        oldClientSentByteCount = client.GetSessionStatus().SessionTraffic.Sent;
        oldClientReceivedByteCount = client.GetSessionStatus().SessionTraffic.Received;
        oldServerSentByteCount = serverSession.Tunnel.Traffic.Sent;
        oldServerReceivedByteCount = serverSession.Tunnel.Traffic.Received;

        await TestHelper.Test_Udp();

        Assert.AreNotEqual(oldClientSentByteCount, client.GetSessionStatus().SessionTraffic.Sent, 500,
            "Not enough data has been sent through the client.");
        Assert.AreNotEqual(oldClientReceivedByteCount, client.GetSessionStatus().SessionTraffic.Received, 500,
            "Not enough data has been received through the client.");
        Assert.AreNotEqual(oldServerSentByteCount, serverSession.Tunnel.Traffic.Sent, 500,
            "Not enough data has been sent through the server.");
        Assert.AreNotEqual(oldServerReceivedByteCount, serverSession.Tunnel.Traffic.Received, 500,
            "Not enough data has been received through the server.");

        // ************
        // *** TEST ***: IcmpV4
        oldClientSentByteCount = client.GetSessionStatus().SessionTraffic.Sent;
        oldClientReceivedByteCount = client.GetSessionStatus().SessionTraffic.Received;
        oldServerSentByteCount = serverSession.Tunnel.Traffic.Sent;
        oldServerReceivedByteCount = serverSession.Tunnel.Traffic.Received;

        await TestHelper.Test_Ping(ipAddress: TestConstants.PingV4Address1);

        Assert.AreNotEqual(oldClientSentByteCount, client.GetSessionStatus().SessionTraffic.Sent, 500,
            "Not enough data has been sent through the client.");
        Assert.AreNotEqual(oldClientReceivedByteCount, client.GetSessionStatus().SessionTraffic.Received, 500,
            "Not enough data has been received through the client.");
        Assert.AreNotEqual(oldServerSentByteCount, serverSession.Tunnel.Traffic.Sent, 500,
            "Not enough data has been sent through the server.");
        Assert.AreNotEqual(oldServerReceivedByteCount, serverSession.Tunnel.Traffic.Received, 500,
            "Not enough data has been received through the server.");

        if (await TestHelper.IsIpV6Supported()) {
            await TestHelper.Test_Ping(ipAddress: TestConstants.PingV6Address1);

            Assert.AreNotEqual(oldClientSentByteCount, client.GetSessionStatus().SessionTraffic.Sent, 500,
                "Not enough data has been sent through the client.");
            Assert.AreNotEqual(oldClientReceivedByteCount, client.GetSessionStatus().SessionTraffic.Received, 500,
                "Not enough data has been received through the client.");
            Assert.AreNotEqual(oldServerSentByteCount, serverSession.Tunnel.Traffic.Sent, 500,
                "Not enough data has been sent through the server.");
            Assert.AreNotEqual(oldServerReceivedByteCount, serverSession.Tunnel.Traffic.Received, 500,
                "Not enough data has been received through the server.");
        }
    }

    [TestMethod]
    public async Task Client_must_dispose_after_device_closed()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        using var vpnAdapter = new TestNullVpnAdapter();
        await using var client = await TestHelper.CreateClient(token, vpnAdapter);

        vpnAdapter.Dispose();
        await client.WaitForState(ClientState.Disposed);
    }

    [TestMethod]
    public async Task Client_must_dispose_after_server_stopped()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create client
        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.SessionTimeout = TimeSpan.FromSeconds(1);
        await using var client = await TestHelper.CreateClient(
            vpnAdapter: TestHelper.CreateTestVpnAdapter(), clientOptions: clientOptions);

        await TestHelper.Test_Https();

        await server.DisposeAsync();
        try {
            await TestHelper.Test_Https(timeout: 3000);
        }
        catch {
            /* ignored */
        }

        Thread.Sleep(1000);
        try {
            await TestHelper.Test_Https(timeout: 3000);
        }
        catch {
            /* ignored */
        }

        await client.WaitForState( ClientState.Disposed);
        await client.DisposeAsync();
    }

    [TestMethod]
    public async Task Datagram_channel_after_client_reconnection()
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
            await TestHelper.Test_Udp(udpClient, TestConstants.UdpV4EndPoint1);
        }

        // create client
        await using (await TestHelper.CreateClient(token, vpnAdapter: TestHelper.CreateTestVpnAdapter())) {
            // test Icmp & Udp
            await TestHelper.Test_Ping(ping);
            await TestHelper.Test_Udp(udpClient, TestConstants.UdpV4EndPoint1);
        }
    }

    [TestMethod]
    public async Task Reset_tcp_connection_immediately_after_vpn_connected()
    {
        // create server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        using TcpClient tcpClient = new(TestConstants.HttpsUri1.Host, 443);
        await using var stream = tcpClient.GetStream();

        // create client
        await using var client1 = await TestHelper.CreateClient(token);

        try {
            stream.WriteByte(1);
            stream.ReadByte();
            Assert.Fail("Exception expected!");
        }
        catch (Exception ex)
            when (ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset }) {
            // OK
        }
    }

    [TestMethod]
    public async Task Disconnect_if_session_expired()
    {
        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);

        // connect
        await using var client = await TestHelper.CreateClient(token, vpnAdapter: TestHelper.CreateTestVpnAdapter());
        Assert.AreEqual(ClientState.Connected, client.State);

        // close session
        VhLogger.Instance.LogTrace(GeneralEventId.Test, "Closing the session by Test.");
        await server.SessionManager.CloseSession(client.SessionId);

        // wait for disposing session in access server
        await VhTestUtil.AssertEqualsWait(false,
            () => accessManager.SessionService.Sessions.TryGetValue(client.SessionId, out var session) &&
                  session.IsAlive,
            "Session has not been closed in the access server.");

        try {
            await TestHelper.Test_Https();
        }
        catch {
            // ignored
        }

        await client.WaitForState( ClientState.Disposed);
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
        accessManager.EmbedIoAccessManager.Stop();
        await using var server2 = await TestHelper.CreateServer(accessManager, false);
        await server2.Start();

        // ----------
        // Check: MaintenanceMode is expected
        // ----------
        var token = TestHelper.CreateAccessToken(server);
        await using var client =
            await TestHelper.CreateClient(token, autoConnect: false, vpnAdapter: new TestNullVpnAdapter());
        await Assert.ThrowsExceptionAsync<MaintenanceException>(() => client.Connect());

        Assert.AreEqual(SessionErrorCode.Maintenance, client.GetLastSessionErrorCode());
        Assert.AreEqual(ClientState.Disposed, client.State);

        // ----------
        // Check: Connect after Maintenance is done
        // ----------
        accessManager.EmbedIoAccessManager.Start();
        await using var client2 = await TestHelper.CreateClient(token, vpnAdapter: new TestNullVpnAdapter());
        await client2.WaitForState( ClientState.Connected);

        // ----------
        // Check: Go Maintenance mode after server started by stopping the server
        // ----------
        accessManager.EmbedIoAccessManager.Stop();
        await using var client3 =
            await TestHelper.CreateClient(token, autoConnect: false, vpnAdapter: new TestNullVpnAdapter());
        await Assert.ThrowsExceptionAsync<MaintenanceException>(() => client3.Connect());

        await client3.WaitForState(ClientState.Disposed);
        Assert.AreEqual(SessionErrorCode.Maintenance, client3.GetLastSessionErrorCode());

        // ----------
        // Check: Connect after Maintenance is done
        // ----------
        accessManager.EmbedIoAccessManager.Start();
        await using var client4 = await TestHelper.CreateClient(token, vpnAdapter: new TestNullVpnAdapter());
        await client4.WaitForState(ClientState.Connected);

        // ----------
        // Check: Go Maintenance mode by replying 404 from access-server
        // ----------
        accessManager.EmbedIoAccessManager.HttpException = HttpException.Forbidden();
        await using var client5 =
            await TestHelper.CreateClient(token, autoConnect: false, vpnAdapter: new TestNullVpnAdapter());
        await Assert.ThrowsExceptionAsync<MaintenanceException>(() => client5.Connect());

        await client5.WaitForState(ClientState.Disposed);
        Assert.AreEqual(SessionErrorCode.Maintenance, client5.GetLastSessionErrorCode());

        // ----------
        // Check: Connect after Maintenance is done
        // ----------
        accessManager.EmbedIoAccessManager.HttpException = null;
        await using var client6 = await TestHelper.CreateClient(token, vpnAdapter: new TestNullVpnAdapter());
        await client6.WaitForState( ClientState.Connected);
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

        await Assert.ThrowsExceptionAsync<SessionException>(() => client.Connect());
        Assert.AreEqual(SessionErrorCode.UnsupportedClient, client.GetLastSessionErrorCode());
    }

    [TestMethod]
    public async Task Server_limit_by_Max_TcpConnectWait()
    {
        // create access server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.MaxTcpConnectWaitCount = 2;
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token);

        using var httpClient = new HttpClient();
        _ = httpClient.GetStringAsync($"https://{TestConstants.InvalidIp}:4441");
        _ = httpClient.GetStringAsync($"https://{TestConstants.InvalidIp}:4442");
        _ = httpClient.GetStringAsync($"https://{TestConstants.InvalidIp}:4443");
        _ = httpClient.GetStringAsync($"https://{TestConstants.InvalidIp}:4445");

        await Task.Delay(1000);
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
        await using var client = await TestHelper.CreateClient(token);

        using var tcpClient1 = new TcpClient();
        using var tcpClient2 = new TcpClient();
        using var tcpClient3 = new TcpClient();
        using var tcpClient4 = new TcpClient();

        await tcpClient1.ConnectAsync(TestConstants.TcpEndPoint1);
        await Task.Delay(300);
        await tcpClient2.ConnectAsync(TestConstants.TcpEndPoint1);
        await Task.Delay(300);
        await tcpClient3.ConnectAsync(TestConstants.TcpEndPoint2);
        await Task.Delay(300);
        await tcpClient4.ConnectAsync(TestConstants.TcpEndPoint2);
        await Task.Delay(300);

        var session = server.SessionManager.GetSessionById(client.SessionId);
        Assert.AreEqual(fileAccessManagerOptions.SessionOptions.MaxTcpChannelCount, session?.TcpChannelCount);
    }

    [TestMethod]
    public async Task Reusing_ChunkStream()
    {
        // Create Server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        await using var client =
            await TestHelper.CreateClient(clientOptions: TestHelper.CreateClientOptions(token, useUdpChannel: true));
        var lasCreatedConnectionCount = client.GetSessionStatus().ConnectorStat.CreatedConnectionCount;
        var lasReusedConnectionSucceededCount = client.GetSessionStatus().ConnectorStat.ReusedConnectionSucceededCount;

        // create one connection
        await TestHelper.Test_Https();
        Assert.AreEqual(lasReusedConnectionSucceededCount,
            client.GetSessionStatus().ConnectorStat.ReusedConnectionSucceededCount);
        Assert.AreEqual(lasCreatedConnectionCount + 1, client.GetSessionStatus().ConnectorStat.CreatedConnectionCount);
        lasCreatedConnectionCount = client.GetSessionStatus().ConnectorStat.CreatedConnectionCount;
        lasReusedConnectionSucceededCount = client.GetSessionStatus().ConnectorStat.ReusedConnectionSucceededCount;
        await VhTestUtil.AssertEqualsWait(1, () => client.GetSessionStatus().ConnectorStat.FreeConnectionCount);

        // this connection must reuse the old one
        await TestHelper.Test_Https();
        Assert.AreEqual(lasCreatedConnectionCount, client.GetSessionStatus().ConnectorStat.CreatedConnectionCount);
        Assert.AreEqual(lasReusedConnectionSucceededCount + 1,
            client.GetSessionStatus().ConnectorStat.ReusedConnectionSucceededCount);
        lasCreatedConnectionCount = client.GetSessionStatus().ConnectorStat.CreatedConnectionCount;
        lasReusedConnectionSucceededCount = client.GetSessionStatus().ConnectorStat.ReusedConnectionSucceededCount;
        await VhTestUtil.AssertEqualsWait(1, () => client.GetSessionStatus().ConnectorStat.FreeConnectionCount);

        // this connection must reuse the old one again
        await TestHelper.Test_Https();
        Assert.AreEqual(lasCreatedConnectionCount, client.GetSessionStatus().ConnectorStat.CreatedConnectionCount);
        Assert.AreEqual(lasReusedConnectionSucceededCount + 1,
            client.GetSessionStatus().ConnectorStat.ReusedConnectionSucceededCount);
        lasCreatedConnectionCount = client.GetSessionStatus().ConnectorStat.CreatedConnectionCount;
        lasReusedConnectionSucceededCount = client.GetSessionStatus().ConnectorStat.ReusedConnectionSucceededCount;
        await VhTestUtil.AssertEqualsWait(1, () => client.GetSessionStatus().ConnectorStat.FreeConnectionCount);

        // open 3 connections simultaneously
        VhLogger.Instance.LogTrace("Test: Open 3 connections simultaneously.");
        using (var tcpClient1 = new TcpClient())
        using (var tcpClient2 = new TcpClient())
        using (var tcpClient3 = new TcpClient()) {
            await tcpClient1.ConnectAsync(TestConstants.HttpsEndPoint1);
            await tcpClient2.ConnectAsync(TestConstants.HttpsEndPoint1);
            await tcpClient3.ConnectAsync(TestConstants.HttpsEndPoint1);

            await VhTestUtil.AssertEqualsWait(lasCreatedConnectionCount + 2,
                () => client.GetSessionStatus().ConnectorStat.CreatedConnectionCount);
            await VhTestUtil.AssertEqualsWait(lasReusedConnectionSucceededCount + 1,
                () => client.GetSessionStatus().ConnectorStat.ReusedConnectionSucceededCount);
            lasCreatedConnectionCount = client.GetSessionStatus().ConnectorStat.CreatedConnectionCount;
            lasReusedConnectionSucceededCount = client.GetSessionStatus().ConnectorStat.ReusedConnectionSucceededCount;
        }

        VhLogger.Instance.LogTrace(GeneralEventId.Test, "Test: Waiting for free connections...");
        await VhTestUtil.AssertEqualsWait(3, () => client.GetSessionStatus().ConnectorStat.FreeConnectionCount);

        // net two connection should use shared connection
        using (var tcpClient4 = new TcpClient())
        using (var tcpClient5 = new TcpClient()) {
            await tcpClient4.ConnectAsync(TestConstants.HttpsEndPoint1);
            await tcpClient5.ConnectAsync(TestConstants.HttpsEndPoint2);
            await VhTestUtil.AssertEqualsWait(lasCreatedConnectionCount,
                () => client.GetSessionStatus().ConnectorStat.CreatedConnectionCount);
            await VhTestUtil.AssertEqualsWait(lasReusedConnectionSucceededCount + 2,
                () => client.GetSessionStatus().ConnectorStat.ReusedConnectionSucceededCount);
        }

        // wait for free the used connections 
        await VhTestUtil.AssertEqualsWait(3, () => client.GetSessionStatus().ConnectorStat.FreeConnectionCount);
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
            clientOptions: TestHelper.CreateClientOptions(token: token, useUdpChannel: true));

        Assert.IsFalse(client.SessionInfo?.IsUdpChannelSupported);
    }

    [TestMethod]
    public async Task TunProvider_by_udp()
    {
        using var tunProvider = new TestUdpTunProvider();

        // create access server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions, tunProvider: tunProvider);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token);

        // test udp
        await TestHelper.Test_Udp(TestConstants.UdpV4EndPoint1);
        await TestHelper.Test_Udp(TestConstants.UdpV4EndPoint2);
    }

    [TestMethod]
    public async Task Set_DnsServer_to_vpnAdapter()
    {
        // Create Server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create app
        await using var client = await TestHelper.CreateClient(token, vpnAdapter: new TestNullVpnAdapter());
        await client.WaitForState( ClientState.Connected);

        Assert.IsTrue(client.DnsServers is { Length: > 0 });
    }
}