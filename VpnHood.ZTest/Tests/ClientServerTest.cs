using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EmbedIO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Server;
using VpnHood.Server.Providers.FileAccessServerProvider;

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
    public void Redirect_Server()
    {
        var serverEndPoint1 = Util.GetFreeEndPoint(IPAddress.Loopback);
        var fileAccessServerOptions1 = new FileAccessServerOptions { TcpEndPoints = new[] { serverEndPoint1 } };
        using var fileAccessServer1 = TestHelper.CreateFileAccessServer(fileAccessServerOptions1);
        using var testAccessServer1 = new TestAccessServer(fileAccessServer1);
        using var server1 = TestHelper.CreateServer(testAccessServer1);

        // Create Server 2
        var serverEndPoint2 = Util.GetFreeEndPoint(IPAddress.Loopback);
        var fileAccessServerOptions2 = new FileAccessServerOptions { TcpEndPoints = new[] { serverEndPoint2 } };
        using var fileAccessServer2 = TestHelper.CreateFileAccessServer(fileAccessServerOptions2, fileAccessServer1.StoragePath);
        using var testAccessServer2 = new TestAccessServer(fileAccessServer2);
        using var server2 = TestHelper.CreateServer(testAccessServer2);

        // redirect server1 to server2
        testAccessServer1.EmbedIoAccessServer.RedirectHostEndPoint = serverEndPoint2;

        // Create Client
        var token1 = TestHelper.CreateAccessToken(fileAccessServer1, new[] { serverEndPoint1 });
        using var client = TestHelper.CreateClient(token1);
        TestHelper.Test_Https();

        Assert.AreEqual(serverEndPoint2, client.HostEndPoint);
    }

    [TestMethod]
    public void TcpChannel()
    {
        // Create Server
        var serverEp = Util.GetFreeEndPoint(IPAddress.IPv6Loopback);
        var fileAccessServerOptions = new FileAccessServerOptions { TcpEndPoints = new[] { serverEp } };
        using var fileAccessServer = TestHelper.CreateFileAccessServer(fileAccessServerOptions);
        using var testAccessServer = new TestAccessServer(fileAccessServer);
        using var server = TestHelper.CreateServer(testAccessServer);
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        using var client = TestHelper.CreateClient(token, options: new ClientOptions { UseUdpChannel = false });

        TestTunnel(server, client);

        // check HostEndPoint in server
        fileAccessServer.SessionManager.Sessions.TryGetValue(client.SessionId, out var session);
        Assert.IsTrue(token.HostEndPoints?.Any(x => x.Equals(session?.HostEndPoint)));

        // check UserAgent in server
        Assert.AreEqual(client.UserAgent, session?.ClientInfo.UserAgent);

        // check ClientPublicAddress in server
        Assert.AreEqual(serverEp.Address, client.PublicAddress);
    }

    [TestMethod]
    public async Task MaxDatagramChannels()
    {
        var fileAccessServerOptions = TestHelper.CreateFileAccessServerOptions();
        fileAccessServerOptions.SessionOptions.MaxDatagramChannelCount = 3;

        // Create Server
        await using var server = TestHelper.CreateServer(fileAccessServerOptions);
        var token = TestHelper.CreateAccessToken(server);

        // --------
        // Check: Client MaxDatagramChannelCount larger than server
        // --------
        await using var client = TestHelper.CreateClient(token, options: new ClientOptions
        {
            UseUdpChannel = false,
            MaxDatagramChannelCount = 6
        });
        TestHelper.Test_Udp();
        Thread.Sleep(1000);
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
        TestHelper.Test_Udp();
        Thread.Sleep(1000);
        Assert.AreEqual(1, client2.DatagramChannelsCount);
        await client.DisposeAsync();
    }

    [TestMethod]
    public void UnsupportedClient()
    {
        // Create Server
        using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        using var client = TestHelper.CreateClient(token, options: new ClientOptions { UseUdpChannel = true });
    }

    [TestMethod]
    public void UdpChannel()
    {
        // Create Server
        using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        using var client = TestHelper.CreateClient(token, options: new ClientOptions { UseUdpChannel = true });
        TestTunnel(server, client);
        Assert.IsTrue(client.UseUdpChannel);

        // switch to tcp
        client.UseUdpChannel = false;
        TestTunnel(server, client);
        Assert.IsFalse(client.UseUdpChannel);

        // switch back to udp
        client.UseUdpChannel = true;
        TestTunnel(server, client);
        Assert.IsTrue(client.UseUdpChannel);
    }

    private static void TestTunnel(VpnHoodServer server, VpnHoodClient client)
    {
        Assert.AreEqual(ServerState.Ready, server.State);
        Assert.AreEqual(ClientState.Connected, client.State);

        // Get session
        server.SessionManager.Sessions.TryGetValue(client.SessionId, out var serverSession);
        Assert.IsNotNull(serverSession, "Could not find session in server!");

        // ************
        // *** TEST ***: TCP invalid request should not close the vpn connection
        var oldClientSentByteCount = client.SentByteCount;
        var oldClientReceivedByteCount = client.ReceivedByteCount;
        var oldServerSentByteCount = serverSession.Tunnel.SentByteCount;
        var oldServerReceivedByteCount = serverSession.Tunnel.ReceivedByteCount;

        using var httpClient = new HttpClient();
        try
        {
            httpClient.GetStringAsync($"http://{TestHelper.TEST_NsEndPoint1}:4/").Wait();
            Assert.Fail("Exception expected!");
        }
        catch
        {
            // ignored
        }

        Assert.AreEqual(ClientState.Connected, client.State);

        // ************
        // *** TEST ***: TCP (TLS) by quad9
        TestHelper.Test_Https();

        // check there is send data
        Assert.IsTrue(client.SentByteCount > oldClientSentByteCount + 100,
            "Not enough data has been sent through the client!");
        Assert.IsTrue(client.ReceivedByteCount > oldClientReceivedByteCount + 2000,
            "Not enough data has been sent through the client!");
        Assert.IsTrue(serverSession.Tunnel.SentByteCount > oldServerSentByteCount + 2000,
            "Not enough data has been sent through the client!");
        Assert.IsTrue(serverSession.Tunnel.ReceivedByteCount > oldServerReceivedByteCount + 100,
            "Not enough data has been sent through the client!");

        // ************
        // *** TEST ***: UDP
        oldClientSentByteCount = client.SentByteCount;
        oldClientReceivedByteCount = client.ReceivedByteCount;
        oldServerSentByteCount = serverSession.Tunnel.SentByteCount;
        oldServerReceivedByteCount = serverSession.Tunnel.ReceivedByteCount;

        TestHelper.Test_Udp();

        Assert.IsTrue(client.SentByteCount > oldClientSentByteCount + 10,
            "Not enough data has been sent through the client!");
        Assert.IsTrue(client.ReceivedByteCount > oldClientReceivedByteCount + 10,
            "Not enough data has been sent through the client!");
        Assert.IsTrue(serverSession.Tunnel.SentByteCount > oldServerSentByteCount + 10,
            "Not enough data has been sent through the client!");
        Assert.IsTrue(serverSession.Tunnel.ReceivedByteCount > oldServerReceivedByteCount + 10,
            "Not enough data has been sent through the client!");

        // ************
        // *** TEST ***: Icmp
        oldClientSentByteCount = client.SentByteCount;
        oldClientReceivedByteCount = client.ReceivedByteCount;
        oldServerSentByteCount = serverSession.Tunnel.SentByteCount;
        oldServerReceivedByteCount = serverSession.Tunnel.ReceivedByteCount;

        TestHelper.Test_Ping();

        Assert.IsTrue(client.SentByteCount > oldClientSentByteCount + 100,
            "Not enough data has been sent through the client!");
        Assert.IsTrue(client.ReceivedByteCount > oldClientReceivedByteCount + 100,
            "Not enough data has been sent through the client!");
        Assert.IsTrue(serverSession.Tunnel.SentByteCount > oldServerSentByteCount + 100,
            "Not enough data has been sent through the client!");
        Assert.IsTrue(serverSession.Tunnel.ReceivedByteCount > oldServerReceivedByteCount + 100,
            "Not enough data has been sent through the client!");
    }

    [TestMethod]
    public void Client_must_dispose_after_device_closed()
    {
        using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        using var packetCapture = TestHelper.CreatePacketCapture();
        using var client = TestHelper.CreateClient(token, packetCapture);

        packetCapture.StopCapture();
        TestHelper.WaitForClientState(client, ClientState.Disposed);
    }

    [TestMethod]
    public void Client_must_dispose_after_server_stopped()
    {
        using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create client
        using var client = TestHelper.CreateClient(token, options: new ClientOptions { SessionTimeout = TimeSpan.FromSeconds(1) });
        TestHelper.Test_Https();

        server.Dispose();
        try { TestHelper.Test_Https(); } catch { /* ignored */ }
        Thread.Sleep(1000);
        try { TestHelper.Test_Https(); } catch { /* ignored */ }

        TestHelper.WaitForClientState(client, ClientState.Disposed);
    }

    [TestMethod]
    public void Datagram_channel_after_client_reconnection()
    {
        //create a shared udp client among connection
        // make sure using same local port to test Nat properly
        using var udpClient = new UdpClient();
        using var ping = new Ping();

        using var fileAccessServer = TestHelper.CreateFileAccessServer();
        using var testAccessServer = new TestAccessServer(fileAccessServer);
        using var server = TestHelper.CreateServer(testAccessServer);
        var token = TestHelper.CreateAccessToken(server);

        // create client
        using var client1 = TestHelper.CreateClient(token);

        // test Icmp & Udp
        TestHelper.Test_Ping(ping);
        TestHelper.Test_Udp(udpClient);

        // create client
        using var client2 = TestHelper.CreateClient(token);

        // test Icmp & Udp
        TestHelper.Test_Ping(ping);
        TestHelper.Test_Udp(udpClient);
    }

    [TestMethod]
    public void Restore_session_after_restarting_server()
    {
        using var fileAccessServer = TestHelper.CreateFileAccessServer();
        using var testAccessServer = new TestAccessServer(fileAccessServer);

        // create server
        using var server = TestHelper.CreateServer(testAccessServer);
        var token = TestHelper.CreateAccessToken(server);

        using var client = TestHelper.CreateClient(token);
        Assert.AreEqual(ClientState.Connected, client.State);

        // disconnect server
        server.Dispose();
        try { TestHelper.Test_Https(); } catch { /* ignored */ }
        Assert.AreEqual(ClientState.Connecting, client.State);

        // recreate server and reconnect
        using var server2 = TestHelper.CreateServer(testAccessServer);
        TestHelper.Test_Https();

    }


    [TestMethod]
    public void Reset_tcp_connection_immediately_after_vpn_connected()
    {
        // create server
        using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        using TcpClient tcpClient = new(TestHelper.TEST_HttpsUri1.Host, 443);
        using var stream = tcpClient.GetStream();

        // create client
        using var client1 = TestHelper.CreateClient(token);

        try
        {
            stream.WriteByte(1);
            stream.ReadByte();
        }
        catch (Exception ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset })
        {
            // OK
        }
    }

    [TestMethod]
    public async Task Disconnect_if_session_expired()
    {
        using var fileAccessServer = TestHelper.CreateFileAccessServer();
        using var testAccessServer = new TestAccessServer(fileAccessServer);

        // create server
        await using var server = TestHelper.CreateServer(testAccessServer);
        var token = TestHelper.CreateAccessToken(server);

        // connect
        await using var client = TestHelper.CreateClient(token);
        Assert.AreEqual(ClientState.Connected, client.State);

        // restart server
        await server.SessionManager.CloseSession(client.SessionId);

        // wait for disposing session in access server
        for (var i = 0; i < 6; i++)
        {
            if (!fileAccessServer.SessionManager.Sessions.TryGetValue(client.SessionId, out var session) ||
                !session.IsAlive)
                break;
            Thread.Sleep(200);
        }

        try
        {
            await TestHelper.Test_HttpsAsync();
        }
        catch
        {
            // ignored
        }

        TestHelper.WaitForClientState(client, ClientState.Disposed, 5000);
    }

    [TestMethod]
    public async Task Configure_Maintenance_Server()
    {
        // --------
        // Check: AccessServer is on at start
        // --------
        using var fileAccessServer = TestHelper.CreateFileAccessServer();
        using var testAccessServer = new TestAccessServer(fileAccessServer);
        await using var server = TestHelper.CreateServer(testAccessServer);

        Assert.IsFalse(server.AccessServer.IsMaintenanceMode);
        Assert.AreEqual(Environment.Version, fileAccessServer.ServerInfo?.EnvironmentVersion);
        Assert.AreEqual(Environment.MachineName, fileAccessServer.ServerInfo?.MachineName);
        Assert.IsTrue(fileAccessServer.ServerStatus?.ThreadCount > 0);
        await server.DisposeAsync();

        // ------------
        // Check: AccessServer is off at start
        // ------------
        testAccessServer.EmbedIoAccessServer.Stop();
        await using var server2 = TestHelper.CreateServer(testAccessServer, false);
        await server2.Start();

        // ----------
        // Check: MaintenanceMode is expected
        // ----------
        var token = TestHelper.CreateAccessToken(fileAccessServer);
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
        testAccessServer.EmbedIoAccessServer.Start();
        await using var client2 = TestHelper.CreateClient(token);
        TestHelper.WaitForClientState(client2, ClientState.Connected);

        // ----------
        // Check: Go Maintenance mode after server started by stopping the server
        // ----------
        testAccessServer.EmbedIoAccessServer.Stop();
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
        TestHelper.WaitForClientState(client3, ClientState.Disposed);
        Assert.AreEqual(SessionErrorCode.Maintenance, client3.SessionStatus.ErrorCode);

        // ----------
        // Check: Connect after Maintenance is done
        // ----------
        testAccessServer.EmbedIoAccessServer.Start();
        await using var client4 = TestHelper.CreateClient(token);
        TestHelper.WaitForClientState(client4, ClientState.Connected);

        // ----------
        // Check: Go Maintenance mode by replying 404 from access-server
        // ----------
        testAccessServer.EmbedIoAccessServer.HttpException = HttpException.Forbidden();
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
        TestHelper.WaitForClientState(client5, ClientState.Disposed);
        Assert.AreEqual(SessionErrorCode.Maintenance, client5.SessionStatus.ErrorCode);

        // ----------
        // Check: Connect after Maintenance is done
        // ----------
        testAccessServer.EmbedIoAccessServer.HttpException = null;
        await using var client6 = TestHelper.CreateClient(token);
        TestHelper.WaitForClientState(client6, ClientState.Connected);
    }

    [TestMethod]
    public async Task AutoReconnect()
    {
        using var httpClient = new HttpClient();

        // create server
        using var fileAccessServer = TestHelper.CreateFileAccessServer();
        using var testAccessServer = new TestAccessServer(fileAccessServer);
        await using var server = TestHelper.CreateServer(testAccessServer);

        // create client
        var token = TestHelper.CreateAccessToken(server);

        // -----------
        // Check:  Reconnect after disconnection (1st time)
        // -----------
        await using var clientConnect = TestHelper.CreateClientConnect(token,
            connectOptions: new ConnectOptions { MaxReconnectCount = 1, ReconnectDelay = TimeSpan.Zero });
        Assert.AreEqual(ClientState.Connected, clientConnect.Client.State); // checkpoint
        await TestHelper.Test_HttpsAsync(); //let transfer something


        fileAccessServer.SessionManager.Sessions.TryRemove(clientConnect.Client.SessionId, out _);
        server.SessionManager.Sessions.TryRemove(clientConnect.Client.SessionId, out _);

        try
        {
            await TestHelper.Test_HttpsAsync();
        }
        catch
        {
            // ignored
        }

        TestHelper.WaitForClientState(clientConnect.Client, ClientState.Connected);
        Assert.AreEqual(1, clientConnect.AttemptCount);
        TestTunnel(server, clientConnect.Client);

        // ************
        // *** TEST ***: dispose after second try (2st time)
        Assert.AreEqual(ClientState.Connected, clientConnect.Client.State); // checkpoint
        await server.SessionManager.CloseSession(clientConnect.Client.SessionId);

        try
        {
            await TestHelper.Test_HttpsAsync();
        }
        catch
        {
            // ignored
        }

        TestHelper.WaitForClientState(clientConnect.Client, ClientState.Disposed);
        Assert.AreEqual(1, clientConnect.AttemptCount);
    }

    [TestMethod]
    public async Task Close_session_by_client_disconnect()
    {
        // create server
        using var fileAccessServer = TestHelper.CreateFileAccessServer();
        using var testAccessServer = new TestAccessServer(fileAccessServer);
        await using var server = TestHelper.CreateServer(testAccessServer);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = TestHelper.CreateClient(token);
        Assert.IsTrue(fileAccessServer.SessionManager.Sessions.TryGetValue(client.SessionId, out var session));
        await client.DisposeAsync();
        TestHelper.WaitForClientState(client, ClientState.Disposed);

        Assert.IsFalse(session.IsAlive);
    }

#if DEBUG
    [TestMethod]
    public void Disconnect_for_unsupported_client()
    {
        // create server
        using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create client
        using var client = TestHelper.CreateClient(token, autoConnect: false,
            options: new ClientOptions { ProtocolVersion = 1 });

        try
        {
            var _ = client.Connect();
            TestHelper.WaitForClientState(client, ClientState.Disposed);
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

    [TestMethod]
    public async Task Server_limit_by_Max_TcpConnectWait()
    {
        // create access server
        var fileAccessServerOptions = TestHelper.CreateFileAccessServerOptions();
        fileAccessServerOptions.SessionOptions.MaxTcpConnectWaitCount= 2;
        await using var server = TestHelper.CreateServer(fileAccessServerOptions);

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
        Assert.AreEqual(fileAccessServerOptions.SessionOptions.MaxTcpConnectWaitCount, session?.TcpConnectWaitCount);
    }

    [TestMethod]
    public async Task Server_limit_by_Max_TcpChannel()
    {
        // create access server
        // create access server
        var fileAccessServerOptions = TestHelper.CreateFileAccessServerOptions();
        fileAccessServerOptions.SessionOptions.MaxTcpChannelCount = 2;
        await using var server = TestHelper.CreateServer(fileAccessServerOptions);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = TestHelper.CreateClient(token);

        using var tcpClient1 = new TcpClient();
        using var tcpClient2 = new TcpClient();
        using var tcpClient3 = new TcpClient();
        using var tcpClient4 = new TcpClient();

        await tcpClient1.ConnectAsync(TestHelper.TEST_HttpsUri1.Host, 443);
        await Task.Delay(250);
        await tcpClient2.ConnectAsync(TestHelper.TEST_HttpsUri1.Host, 443);
        await Task.Delay(250);
        await tcpClient3.ConnectAsync(TestHelper.TEST_HttpsUri2.Host, 443);
        await Task.Delay(250);
        await tcpClient4.ConnectAsync(TestHelper.TEST_HttpsUri2.Host, 443);
        await Task.Delay(250);

        var session = server.SessionManager.GetSessionById(client.SessionId);
        Assert.AreEqual(fileAccessServerOptions.SessionOptions.MaxTcpChannelCount, session?.TcpChannelCount);
    }

#endif
}