using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EmbedIO;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Server;
using VpnHood.Server.Access.Managers.File;
using VpnHood.Tunneling;

namespace VpnHood.Test.Tests;

[TestClass]
public class ClientServerTest
{
    [TestInitialize]
    public void Initialize()
    {
        VhLogger.Instance = VhLogger.CreateConsoleLogger(true);
    }

    [TestMethod]
    public async Task Redirect_Server()
    {
        var serverEndPoint1 = VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback);
        var fileAccessManagerOptions1 = new FileAccessManagerOptions { TcpEndPoints = new[] { serverEndPoint1 } };
        using var fileAccessManager1 = TestHelper.CreateFileAccessManager(fileAccessManagerOptions1);
        using var testAccessManager1 = new TestAccessManager(fileAccessManager1);
        await using var server1 = TestHelper.CreateServer(testAccessManager1);

        // Create Server 2
        var serverEndPoint2 = VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback);
        var fileAccessManagerOptions2 = new FileAccessManagerOptions { TcpEndPoints = new[] { serverEndPoint2 } };
        using var fileAccessManager2 =
            TestHelper.CreateFileAccessManager(fileAccessManagerOptions2, fileAccessManager1.StoragePath);
        using var testAccessManager2 = new TestAccessManager(fileAccessManager2);
        await using var server2 = TestHelper.CreateServer(testAccessManager2);

        // redirect server1 to server2
        testAccessManager1.EmbedIoAccessManager.RedirectHostEndPoint = serverEndPoint2;

        // Create Client
        var token1 = TestHelper.CreateAccessToken(fileAccessManager1, new[] { serverEndPoint1 });
        await using var client = TestHelper.CreateClient(token1);
        await TestHelper.Test_Https();

        Assert.AreEqual(serverEndPoint2, client.HostTcpEndPoint);
    }

    [TestMethod]
    public async Task TcpChannel()
    {
        // Create Server
        var serverEp = VhUtil.GetFreeTcpEndPoint(IPAddress.IPv6Loopback);
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.TcpEndPoints = new[] { serverEp };
        using var fileAccessManager = TestHelper.CreateFileAccessManager(fileAccessManagerOptions);
        using var testAccessManager = new TestAccessManager(fileAccessManager);
        await using var server = TestHelper.CreateServer(testAccessManager);
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        await using var client = TestHelper.CreateClient(token, options: new ClientOptions { UseUdpChannel = false });

        await TestTunnel(server, client);

        // check HostEndPoint in server
        fileAccessManager.SessionController.Sessions.TryGetValue(client.SessionId, out var session);
        Assert.IsTrue(token.HostEndPoints?.Any(x => x.Equals(session?.HostEndPoint)));

        // check UserAgent in server
        Assert.AreEqual(client.UserAgent, session?.ClientInfo.UserAgent);

        // check ClientPublicAddress in server
        Assert.AreEqual(serverEp.Address, client.PublicAddress);
    }

    [TestMethod]
    public async Task MaxDatagramChannels()
    {
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.MaxDatagramChannelCount = 3;

        // Create Server
        await using var server = TestHelper.CreateServer(fileAccessManagerOptions);
        var token = TestHelper.CreateAccessToken(server);

        // --------
        // Check: Client MaxDatagramChannelCount larger than server
        // --------
        await using var client = TestHelper.CreateClient(token, options: new ClientOptions
        {
            UseUdpChannel = false,
            MaxDatagramChannelCount = 6
        });

        // let channel be created gradually
        for (var i = 0; i < 6; i++)
        {
            await TestHelper.Test_Udp(TestHelper.TEST_UdpV4EndPoint1);
            await Task.Delay(50);
        }

        Thread.Sleep(100);
        Assert.AreEqual(3, client.DatagramChannelsCount);
        await client.DisposeAsync();

        // --------
        // Check: Client MaxDatagramChannelCount smaller than server
        // --------
        await using var client2 = TestHelper.CreateClient(token, options: new ClientOptions
        {
            UseUdpChannel = false,
            MaxDatagramChannelCount = 1
        });

        // let channel be removed gradually
        for (var i = 0; i < 6; i++)
        {
            await TestHelper.Test_Udp(TestHelper.TEST_UdpV4EndPoint1);
            await Task.Delay(50);
        }

        Thread.Sleep(200);
        Assert.AreEqual(1, client2.DatagramChannelsCount);
        await client.DisposeAsync();
    }

    [TestMethod]
    public async Task UnsupportedClient()
    {
        // Create Server
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        await using var client = TestHelper.CreateClient(token, options: new ClientOptions { UseUdpChannel = true });
    }

    [TestMethod]
    public async Task DatagramChannel_Stream()
    {
        VhLogger.IsDiagnoseMode = true;

        // Create Server
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        await using var client = TestHelper.CreateClient(token, options: new ClientOptions
        {
            UseUdpChannel = false,
            MaxDatagramChannelCount = 4
        });

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
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        await using var client = TestHelper.CreateClient(token, options: new ClientOptions
        {
            UseUdpChannel = true,
        });

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
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        await using var client = TestHelper.CreateClient(token, options: new ClientOptions { UseUdpChannel = true });
        VhLogger.Instance.LogTrace(GeneralEventId.Test, "Test: Testing by UdpChannel.");
        await TestTunnel(server, client);
        Assert.IsTrue(client.UseUdpChannel);

        // switch to tcp
        VhLogger.Instance.LogTrace(GeneralEventId.Test, "Test: Switch to DatagramChannel.");
        client.UseUdpChannel = false;
        await TestTunnel(server, client);
        Assert.IsFalse(client.UseUdpChannel);

        // switch back to udp
        VhLogger.Instance.LogTrace(GeneralEventId.Test, "Test: Switch back to UdpChannel.");
        client.UseUdpChannel = true;
        await TestTunnel(server, client);
        Assert.IsTrue(client.UseUdpChannel);
    }

    [TestMethod]
    public async Task UdpChannel_custom_udp_port()
    {
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.UdpEndPoints = fileAccessManagerOptions.UdpEndPoints!
            .Select(x => VhUtil.GetFreeUdpEndPoint(x.Address)).ToArray();

        // Create Server
        await using var server = TestHelper.CreateServer(fileAccessManagerOptions);
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        await using var client = TestHelper.CreateClient(token, options: new ClientOptions { UseUdpChannel = true });
        await TestTunnel(server, client);
        Assert.IsTrue(fileAccessManagerOptions.UdpEndPoints.Any(x => x.Port == client.HostUdpEndPoint?.Port));
    }

    private static async Task TestTunnel(VpnHoodServer server, VpnHoodClient client)
    {
        Assert.AreEqual(ServerState.Ready, server.State);
        Assert.AreEqual(ClientState.Connected, client.State);

        // Get session
        server.SessionManager.Sessions.TryGetValue(client.SessionId, out var serverSession);
        Assert.IsNotNull(serverSession, "Could not find session in server!");

        // ************
        // *** TEST ***: TCP invalid request should not close the vpn connection
        var oldClientSentByteCount = client.Stat.SessionTraffic.Sent;
        var oldClientReceivedByteCount = client.Stat.SessionTraffic.Received;
        var oldServerSentByteCount = serverSession.Tunnel.Traffic.Sent;
        var oldServerReceivedByteCount = serverSession.Tunnel.Traffic.Received;

        using var httpClient = new HttpClient();
        try
        {
            await httpClient.GetStringAsync($"http://{TestHelper.TEST_NsEndPoint1}",
                new CancellationTokenSource(2000).Token);
            Assert.Fail("Exception expected!");
        }
        catch
        {
            // ignored
        }

        Assert.AreEqual(ClientState.Connected, client.State);

        // ************
        // *** TEST ***: TCP (TLS) by quad9
        await TestHelper.Test_Https();

        // check there is send data
        Assert.AreNotEqual(oldClientSentByteCount, client.Stat.SessionTraffic.Sent, 100,
            "Not enough data has been sent through the client.");
        Assert.AreNotEqual(oldClientReceivedByteCount, client.Stat.SessionTraffic.Received, 2000,
            "Not enough data has been received through the client.");
        Assert.AreNotEqual(oldServerSentByteCount, serverSession.Tunnel.Traffic.Sent, 2000,
            "Not enough data has been sent through the server.");
        Assert.AreNotEqual(oldServerReceivedByteCount, serverSession.Tunnel.Traffic.Received, 100,
            "Not enough data has been received through the server.");

        // ************
        // *** TEST ***: UDP v4
        oldClientSentByteCount = client.Stat.SessionTraffic.Sent;
        oldClientReceivedByteCount = client.Stat.SessionTraffic.Received;
        oldServerSentByteCount = serverSession.Tunnel.Traffic.Sent;
        oldServerReceivedByteCount = serverSession.Tunnel.Traffic.Received;

        await TestHelper.Test_Udp();

        Assert.AreNotEqual(oldClientSentByteCount, client.Stat.SessionTraffic.Sent, 500,
            "Not enough data has been sent through the client.");
        Assert.AreNotEqual(oldClientReceivedByteCount, client.Stat.SessionTraffic.Received, 500,
            "Not enough data has been received through the client.");
        Assert.AreNotEqual(oldServerSentByteCount, serverSession.Tunnel.Traffic.Sent, 500,
            "Not enough data has been sent through the server.");
        Assert.AreNotEqual(oldServerReceivedByteCount, serverSession.Tunnel.Traffic.Received, 500,
            "Not enough data has been received through the server.");

        // ************
        // *** TEST ***: IcmpV4
        oldClientSentByteCount = client.Stat.SessionTraffic.Sent;
        oldClientReceivedByteCount = client.Stat.SessionTraffic.Received;
        oldServerSentByteCount = serverSession.Tunnel.Traffic.Sent;
        oldServerReceivedByteCount = serverSession.Tunnel.Traffic.Received;

        await TestHelper.Test_Ping(ipAddress: TestHelper.TEST_PingV4Address1);

        Assert.AreNotEqual(oldClientSentByteCount, client.Stat.SessionTraffic.Sent, 500,
            "Not enough data has been sent through the client.");
        Assert.AreNotEqual(oldClientReceivedByteCount, client.Stat.SessionTraffic.Received, 500,
            "Not enough data has been received through the client.");
        Assert.AreNotEqual(oldServerSentByteCount, serverSession.Tunnel.Traffic.Sent, 500,
            "Not enough data has been sent through the server.");
        Assert.AreNotEqual(oldServerReceivedByteCount, serverSession.Tunnel.Traffic.Received, 500,
            "Not enough data has been received through the server.");
    }

    [TestMethod]
    public async Task Client_must_dispose_after_device_closed()
    {
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        using var packetCapture = TestHelper.CreatePacketCapture();
        await using var client = TestHelper.CreateClient(token, packetCapture);

        packetCapture.StopCapture();
        await TestHelper.WaitForClientStateAsync(client, ClientState.Disposed);
    }

    [TestMethod]
    public async Task Client_must_dispose_after_server_stopped()
    {
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create client
        await using var client = TestHelper.CreateClient(token,
            options: new ClientOptions { SessionTimeout = TimeSpan.FromSeconds(1) });
        await TestHelper.Test_Https();

        await server.DisposeAsync();
        try
        {
            await TestHelper.Test_Https(timeout: 3000);
        }
        catch
        {
            /* ignored */
        }

        Thread.Sleep(1000);
        try
        {
            await TestHelper.Test_Https(timeout: 3000);
        }
        catch
        {
            /* ignored */
        }

        await TestHelper.WaitForClientStateAsync(client, ClientState.Disposed);
        await client.DisposeAsync();
    }

    [TestMethod]
    public async Task Datagram_channel_after_client_reconnection()
    {
        //create a shared udp client among connection
        // make sure using same local port to test Nat properly
        using var udpClient = new UdpClient();
        using var ping = new Ping();

        using var fileAccessManager = TestHelper.CreateFileAccessManager();
        using var testAccessManager = new TestAccessManager(fileAccessManager);
        await using var server = TestHelper.CreateServer(testAccessManager);
        var token = TestHelper.CreateAccessToken(server);

        // create client
        await using (TestHelper.CreateClient(token))
        {
            // test Icmp & Udp
            await TestHelper.Test_Ping(ping);
            await TestHelper.Test_Udp(udpClient, TestHelper.TEST_UdpV4EndPoint1);
        }

        // create client
        await using (TestHelper.CreateClient(token))
        {
            // test Icmp & Udp
            await TestHelper.Test_Ping(ping);
            await TestHelper.Test_Udp(udpClient, TestHelper.TEST_UdpV4EndPoint1);
        }
    }

    [TestMethod]
    public async Task Reset_tcp_connection_immediately_after_vpn_connected()
    {
        // create server
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        using TcpClient tcpClient = new(TestHelper.TEST_HttpsUri1.Host, 443);
        await using var stream = tcpClient.GetStream();

        // create client
        await using var client1 = TestHelper.CreateClient(token);

        try
        {
            stream.WriteByte(1);
            stream.ReadByte();
        }
        catch (Exception ex)
            when (ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset })
        {
            // OK
        }
    }

    [TestMethod]
    public async Task Disconnect_if_session_expired()
    {
        using var fileAccessManager = TestHelper.CreateFileAccessManager();
        using var testAccessManager = new TestAccessManager(fileAccessManager);

        // create server
        await using var server = TestHelper.CreateServer(testAccessManager);
        var token = TestHelper.CreateAccessToken(server);

        // connect
        await using var client = TestHelper.CreateClient(token);
        Assert.AreEqual(ClientState.Connected, client.State);

        // close session
        VhLogger.Instance.LogTrace(GeneralEventId.Test, "Closing the session by Test.");
        await server.SessionManager.CloseSession(client.SessionId);

        // wait for disposing session in access server
        await VhTestUtil.AssertEqualsWait(false,
            () => fileAccessManager.SessionController.Sessions.TryGetValue(client.SessionId, out var session) && session.IsAlive,
            "Session has not been closed in the access server.");

        try
        {
            await TestHelper.Test_Https();
        }
        catch
        {
            // ignored
        }

        await TestHelper.WaitForClientStateAsync(client, ClientState.Disposed);
    }

    [TestMethod]
    public async Task Configure_Maintenance_Server()
    {
        // --------
        // Check: AccessManager is on at start
        // --------
        using var fileAccessManager = TestHelper.CreateFileAccessManager();
        using var testAccessManager = new TestAccessManager(fileAccessManager);
        await using var server = TestHelper.CreateServer(testAccessManager);

        Assert.IsFalse(server.AccessManager.IsMaintenanceMode);
        Assert.AreEqual(Environment.Version, fileAccessManager.ServerInfo?.EnvironmentVersion);
        Assert.AreEqual(Environment.MachineName, fileAccessManager.ServerInfo?.MachineName);
        Assert.IsTrue(fileAccessManager.ServerStatus?.ThreadCount > 0);
        await server.DisposeAsync();

        // ------------
        // Check: AccessManager is off at start
        // ------------
        testAccessManager.EmbedIoAccessManager.Stop();
        await using var server2 = TestHelper.CreateServer(testAccessManager, false);
        await server2.Start();

        // ----------
        // Check: MaintenanceMode is expected
        // ----------
        var token = TestHelper.CreateAccessToken(fileAccessManager);
        await using var client = TestHelper.CreateClient(token, autoConnect: false);
        try
        {
            await client.Connect();
            Assert.Fail("Exception expected!");
        }
        catch (MaintenanceException)
        {
            // ignored
        }

        Assert.AreEqual(SessionErrorCode.Maintenance, client.SessionStatus.ErrorCode);
        Assert.AreEqual(ClientState.Disposed, client.State);

        // ----------
        // Check: Connect after Maintenance is done
        // ----------
        testAccessManager.EmbedIoAccessManager.Start();
        await using var client2 = TestHelper.CreateClient(token);
        await TestHelper.WaitForClientStateAsync(client2, ClientState.Connected);

        // ----------
        // Check: Go Maintenance mode after server started by stopping the server
        // ----------
        testAccessManager.EmbedIoAccessManager.Stop();
        await using var client3 = TestHelper.CreateClient(token, autoConnect: false);
        try
        {
            await client3.Connect();
            Assert.Fail("Exception expected!");
        }
        catch (MaintenanceException)
        {
            // ignored
        }

        await TestHelper.WaitForClientStateAsync(client3, ClientState.Disposed);
        Assert.AreEqual(SessionErrorCode.Maintenance, client3.SessionStatus.ErrorCode);

        // ----------
        // Check: Connect after Maintenance is done
        // ----------
        testAccessManager.EmbedIoAccessManager.Start();
        await using var client4 = TestHelper.CreateClient(token);
        await TestHelper.WaitForClientStateAsync(client4, ClientState.Connected);

        // ----------
        // Check: Go Maintenance mode by replying 404 from access-server
        // ----------
        testAccessManager.EmbedIoAccessManager.HttpException = HttpException.Forbidden();
        await using var client5 = TestHelper.CreateClient(token, autoConnect: false);
        try
        {
            await client5.Connect();
            Assert.Fail("Exception expected!");
        }
        catch (MaintenanceException)
        {
            // ignored
        }

        await TestHelper.WaitForClientStateAsync(client5, ClientState.Disposed);
        Assert.AreEqual(SessionErrorCode.Maintenance, client5.SessionStatus.ErrorCode);

        // ----------
        // Check: Connect after Maintenance is done
        // ----------
        testAccessManager.EmbedIoAccessManager.HttpException = null;
        await using var client6 = TestHelper.CreateClient(token);
        await TestHelper.WaitForClientStateAsync(client6, ClientState.Connected);
    }

    [TestMethod]
    public async Task AutoReconnect()
    {
        using var httpClient = new HttpClient();

        // create server
        using var fileAccessManager = TestHelper.CreateFileAccessManager();
        using var testAccessManager = new TestAccessManager(fileAccessManager);
        await using var server = TestHelper.CreateServer(testAccessManager);

        // create client
        var token = TestHelper.CreateAccessToken(server);

        // -----------
        // Check:  Reconnect after disconnection (1st time)
        // -----------
        await using var clientConnect = TestHelper.CreateClientConnect(token,
            connectOptions: new ConnectOptions { MaxReconnectCount = 1, ReconnectDelay = TimeSpan.Zero });
        Assert.AreEqual(ClientState.Connected, clientConnect.Client.State); // checkpoint
        await TestHelper.Test_Https(); //let transfer something

        fileAccessManager.SessionController.Sessions.TryRemove(clientConnect.Client.SessionId, out _);
        server.SessionManager.Sessions.TryRemove(clientConnect.Client.SessionId, out _);
        await VhTestUtil.AssertEqualsWait(ClientState.Connected, async () =>
        {
            await TestHelper.Test_Https(throwError: false);
            return clientConnect.Client.State;
        }, timeout: 30_000);
        Assert.AreEqual(1, clientConnect.AttemptCount);
        await TestTunnel(server, clientConnect.Client);

        // ************
        // *** TEST ***: dispose after second try (2st time)
        Assert.AreEqual(ClientState.Connected, clientConnect.Client.State); // checkpoint
        await server.SessionManager.CloseSession(clientConnect.Client.SessionId);
        await VhTestUtil.AssertEqualsWait(ClientState.Disposed, async () =>
        {
            await TestHelper.Test_Https(throwError: false);
            return clientConnect.Client.State;
        }, timeout: 30_000);
        Assert.AreEqual(1, clientConnect.AttemptCount);
    }

#if DEBUG
    [TestMethod]
    public async Task Disconnect_for_unsupported_client()
    {
        // create server
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create client
        await using var client = TestHelper.CreateClient(token, autoConnect: false,
            options: new ClientOptions { ProtocolVersion = 1 });

        try
        {
            var _ = client.Connect();
            await TestHelper.WaitForClientStateAsync(client, ClientState.Disposed);
        }
        catch
        {
            // ignored
        }
        finally
        {
            Assert.AreEqual(SessionErrorCode.UnsupportedClient, client.SessionStatus.ErrorCode);
        }
    }
#endif

    [TestMethod]
    public async Task Server_limit_by_Max_TcpConnectWait()
    {
        // create access server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.MaxTcpConnectWaitCount = 2;
        await using var server = TestHelper.CreateServer(fileAccessManagerOptions);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = TestHelper.CreateClient(token);

        using var httpClient = new HttpClient();
        _ = httpClient.GetStringAsync($"https://{TestHelper.TEST_InvalidIp}:4441");
        _ = httpClient.GetStringAsync($"https://{TestHelper.TEST_InvalidIp}:4442");
        _ = httpClient.GetStringAsync($"https://{TestHelper.TEST_InvalidIp}:4443");
        _ = httpClient.GetStringAsync($"https://{TestHelper.TEST_InvalidIp}:4445");

        await Task.Delay(1000);
        var session = server.SessionManager.GetSessionById(client.SessionId);
        Assert.AreEqual(fileAccessManagerOptions.SessionOptions.MaxTcpConnectWaitCount, session?.TcpConnectWaitCount);
    }

    [TestMethod]
    public async Task Server_limit_by_Max_TcpChannel()
    {
        // create access server
        // create access server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.MaxTcpChannelCount = 2;
        await using var server = TestHelper.CreateServer(fileAccessManagerOptions);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = TestHelper.CreateClient(token);

        using var tcpClient1 = new TcpClient();
        using var tcpClient2 = new TcpClient();
        using var tcpClient3 = new TcpClient();
        using var tcpClient4 = new TcpClient();

        await tcpClient1.ConnectAsync(TestHelper.TEST_HttpsUri1.Host, 443);
        await Task.Delay(300);
        await tcpClient2.ConnectAsync(TestHelper.TEST_HttpsUri1.Host, 443);
        await Task.Delay(300);
        await tcpClient3.ConnectAsync(TestHelper.TEST_HttpsUri2.Host, 443);
        await Task.Delay(300);
        await tcpClient4.ConnectAsync(TestHelper.TEST_HttpsUri2.Host, 443);
        await Task.Delay(300);

        var session = server.SessionManager.GetSessionById(client.SessionId);
        Assert.AreEqual(fileAccessManagerOptions.SessionOptions.MaxTcpChannelCount, session?.TcpChannelCount);
    }

    [TestMethod]
    public async Task Reusing_ChunkStream()
    {
        // Create Server
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        await using var client = TestHelper.CreateClient(token, options: TestHelper.CreateClientOptions(useUdp: true));
        var lasCreatedConnectionCount = client.Stat.ConnectorStat.CreatedConnectionCount;
        var lasReusedConnectionSucceededCount = client.Stat.ConnectorStat.ReusedConnectionSucceededCount;

        // create one connection
        await TestHelper.Test_Https();
        Assert.AreEqual(lasReusedConnectionSucceededCount, client.Stat.ConnectorStat.ReusedConnectionSucceededCount);
        Assert.AreEqual(lasCreatedConnectionCount + 1, client.Stat.ConnectorStat.CreatedConnectionCount);
        lasCreatedConnectionCount = client.Stat.ConnectorStat.CreatedConnectionCount;
        lasReusedConnectionSucceededCount = client.Stat.ConnectorStat.ReusedConnectionSucceededCount;
        await VhTestUtil.AssertEqualsWait(1, () => client.Stat.ConnectorStat.FreeConnectionCount);

        // this connection must reuse the old one
        await TestHelper.Test_Https();
        Assert.AreEqual(lasCreatedConnectionCount, client.Stat.ConnectorStat.CreatedConnectionCount);
        Assert.AreEqual(lasReusedConnectionSucceededCount + 1, client.Stat.ConnectorStat.ReusedConnectionSucceededCount);
        lasCreatedConnectionCount = client.Stat.ConnectorStat.CreatedConnectionCount;
        lasReusedConnectionSucceededCount = client.Stat.ConnectorStat.ReusedConnectionSucceededCount;
        await VhTestUtil.AssertEqualsWait(1, () => client.Stat.ConnectorStat.FreeConnectionCount);

        // this connection must reuse the old one again
        await TestHelper.Test_Https();
        Assert.AreEqual(lasCreatedConnectionCount, client.Stat.ConnectorStat.CreatedConnectionCount);
        Assert.AreEqual(lasReusedConnectionSucceededCount + 1, client.Stat.ConnectorStat.ReusedConnectionSucceededCount);
        lasCreatedConnectionCount = client.Stat.ConnectorStat.CreatedConnectionCount;
        lasReusedConnectionSucceededCount = client.Stat.ConnectorStat.ReusedConnectionSucceededCount;
        await VhTestUtil.AssertEqualsWait(1, () => client.Stat.ConnectorStat.FreeConnectionCount);

        // open 3 connections simultaneously
        VhLogger.Instance.LogTrace("Test: Open 3 connections simultaneously.");
        using (var tcpClient1 = new TcpClient())
        using (var tcpClient2 = new TcpClient())
        using (var tcpClient3 = new TcpClient())
        {
            await tcpClient1.ConnectAsync(TestHelper.TEST_HttpsEndPoint1);
            await tcpClient2.ConnectAsync(TestHelper.TEST_HttpsEndPoint1);
            await tcpClient3.ConnectAsync(TestHelper.TEST_HttpsEndPoint1);

            await VhTestUtil.AssertEqualsWait(lasCreatedConnectionCount + 2, () => client.Stat.ConnectorStat.CreatedConnectionCount);
            await VhTestUtil.AssertEqualsWait(lasReusedConnectionSucceededCount + 1, () => client.Stat.ConnectorStat.ReusedConnectionSucceededCount);
            lasCreatedConnectionCount = client.Stat.ConnectorStat.CreatedConnectionCount;
            lasReusedConnectionSucceededCount = client.Stat.ConnectorStat.ReusedConnectionSucceededCount;
        }
        VhLogger.Instance.LogTrace(GeneralEventId.Test, "Test: Waiting for free connections...");
        await VhTestUtil.AssertEqualsWait(3, () => client.Stat.ConnectorStat.FreeConnectionCount);

        // net two connection should use shared connection
        using (var tcpClient4 = new TcpClient())
        using (var tcpClient5 = new TcpClient())
        {
            await tcpClient4.ConnectAsync(TestHelper.TEST_HttpsEndPoint1);
            await tcpClient5.ConnectAsync(TestHelper.TEST_HttpsEndPoint2);
            await VhTestUtil.AssertEqualsWait(lasCreatedConnectionCount, () => client.Stat.ConnectorStat.CreatedConnectionCount);
            await VhTestUtil.AssertEqualsWait(lasReusedConnectionSucceededCount + 2, () => client.Stat.ConnectorStat.ReusedConnectionSucceededCount);
        }

        // wait for free the used connections 
        await VhTestUtil.AssertEqualsWait(3, () => client.Stat.ConnectorStat.FreeConnectionCount);
    }
}