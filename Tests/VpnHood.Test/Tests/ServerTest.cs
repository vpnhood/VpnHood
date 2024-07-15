using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Server.Access.Configurations;
using VpnHood.Test.Device;
using VpnHood.Tunneling;
// ReSharper disable DisposeOnUsingVariable

namespace VpnHood.Test.Tests;

[TestClass]
public class ServerTest : TestBase
{
    [TestMethod]
    public async Task Configure()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        Assert.IsNotNull(accessManager.LastServerInfo);
        Assert.IsTrue(accessManager.LastServerInfo.FreeUdpPortV4 > 0);
        Assert.IsTrue(
            accessManager.LastServerInfo.PrivateIpAddresses.All(
                x => x.AddressFamily != AddressFamily.InterNetworkV6) ||
            accessManager.LastServerInfo?.FreeUdpPortV6 > 0);
    }

    [TestMethod]
    public async Task Auto_sync_sessions_by_interval()
    {
        // Create Server
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.SessionOptions.SyncCacheSize = 10000000;
        serverOptions.SessionOptions.SyncInterval = TimeSpan.FromMicroseconds(200);
        var accessManager = TestHelper.CreateAccessManager(serverOptions);
        await using var server = await TestHelper.CreateServer(accessManager);

        // Create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token, clientOptions: new ClientOptions { UseUdpChannel = true });

        // check usage when usage should be 0
        var sessionResponseEx = await accessManager.Session_Get(client.SessionId, client.HostTcpEndPoint!, null);
        Assert.IsTrue(sessionResponseEx.AccessUsage!.Traffic.Received == 0);

        // lets do transfer
        await TestHelper.Test_Https();

        // check usage should still not be 0 after interval
        await Task.Delay(1000);
        sessionResponseEx = await accessManager.Session_Get(client.SessionId, client.HostTcpEndPoint!, null);
        Assert.IsTrue(sessionResponseEx.AccessUsage!.Traffic.Received > 0);
    }

    [TestMethod]
    public async Task Reconfigure_Listeners()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.ServerConfig.UpdateStatusInterval = TimeSpan.FromMilliseconds(300);
        await using var server = await TestHelper.CreateServer(accessManager);

        // change tcp end points
        var newTcpEndPoint = VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback);
        VhLogger.Instance.LogTrace(GeneralEventId.Test,
            "Test: Changing access server TcpEndPoint. TcpEndPoint: {TcpEndPoint}", newTcpEndPoint);
        accessManager.ServerConfig.TcpEndPoints = [newTcpEndPoint];
        accessManager.ServerConfig.ConfigCode = Guid.NewGuid().ToString();
        await VhTestUtil.AssertEqualsWait(accessManager.ServerConfig.ConfigCode,
            () => accessManager.LastServerStatus!.ConfigCode);
        Assert.AreNotEqual(
            VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback, accessManager.ServerConfig.TcpEndPoints[0].Port),
            accessManager.ServerConfig.TcpEndPoints[0]);

        // change udp end points
        var newUdpEndPoint = VhUtil.GetFreeUdpEndPoint(IPAddress.Loopback);
        VhLogger.Instance.LogTrace(GeneralEventId.Test,
            "Test: Changing access server UdpEndPoint. UdpEndPoint: {UdpEndPoint}", newUdpEndPoint);
        accessManager.ServerConfig.UdpEndPoints = [newUdpEndPoint];
        accessManager.ServerConfig.ConfigCode = Guid.NewGuid().ToString();
        await VhTestUtil.AssertEqualsWait(accessManager.ServerConfig.ConfigCode,
            () => accessManager.LastServerStatus!.ConfigCode); 

        Assert.AreNotEqual(
            VhUtil.GetFreeUdpEndPoint(IPAddress.Loopback, accessManager.ServerConfig.UdpEndPoints[0].Port),
            accessManager.ServerConfig.UdpEndPoints[0]);
    }

    [TestMethod]
    public async Task Reconfigure()
    {
        var serverEndPoint = VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback);
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions(tcpEndPoints: [serverEndPoint]);
        using var accessManager = TestHelper.CreateAccessManager(fileAccessManagerOptions);
        var serverConfig = accessManager.ServerConfig;
        serverConfig.UpdateStatusInterval = TimeSpan.FromMilliseconds(500);
        serverConfig.TrackingOptions.TrackClientIp = true;
        serverConfig.TrackingOptions.TrackLocalPort = true;
        serverConfig.SessionOptions.TcpTimeout = TimeSpan.FromSeconds(2070);
        serverConfig.SessionOptions.UdpTimeout = TimeSpan.FromSeconds(2071);
        serverConfig.SessionOptions.IcmpTimeout = TimeSpan.FromSeconds(2072);
        serverConfig.SessionOptions.Timeout = TimeSpan.FromSeconds(2073);
        serverConfig.SessionOptions.MaxDatagramChannelCount = 2074;
        serverConfig.SessionOptions.SyncCacheSize = 2075;
        serverConfig.SessionOptions.TcpBufferSize = 2076;
        serverConfig.ServerSecret = VhUtil.GenerateKey();

        var dateTime = DateTime.Now;
        await using var server = await TestHelper.CreateServer(accessManager);
        Assert.IsTrue(accessManager.LastConfigureTime > dateTime);

        dateTime = DateTime.Now;
        accessManager.ServerConfig.ConfigCode = Guid.NewGuid().ToString();
        await VhTestUtil.AssertEqualsWait(accessManager.ServerConfig.ConfigCode,
            () => accessManager.LastServerStatus!.ConfigCode);

        CollectionAssert.AreEqual(serverConfig.ServerSecret, server.SessionManager.ServerSecret);
        Assert.IsTrue(accessManager.LastConfigureTime > dateTime);
        Assert.IsTrue(server.SessionManager.TrackingOptions.TrackClientIp);
        Assert.IsTrue(server.SessionManager.TrackingOptions.TrackLocalPort);
        Assert.AreEqual(serverConfig.TrackingOptions.TrackClientIp,
            server.SessionManager.TrackingOptions.TrackClientIp);
        Assert.AreEqual(serverConfig.TrackingOptions.TrackLocalPort,
            server.SessionManager.TrackingOptions.TrackLocalPort);
        Assert.AreEqual(serverConfig.SessionOptions.TcpTimeout, server.SessionManager.SessionOptions.TcpTimeout);
        Assert.AreEqual(serverConfig.SessionOptions.IcmpTimeout, server.SessionManager.SessionOptions.IcmpTimeout);
        Assert.AreEqual(serverConfig.SessionOptions.UdpTimeout, server.SessionManager.SessionOptions.UdpTimeout);
        Assert.AreEqual(serverConfig.SessionOptions.Timeout, server.SessionManager.SessionOptions.Timeout);
        Assert.AreEqual(serverConfig.SessionOptions.MaxDatagramChannelCount,
            server.SessionManager.SessionOptions.MaxDatagramChannelCount);
        Assert.AreEqual(serverConfig.SessionOptions.SyncCacheSize, server.SessionManager.SessionOptions.SyncCacheSize);
        Assert.AreEqual(serverConfig.SessionOptions.TcpBufferSize, server.SessionManager.SessionOptions.TcpBufferSize);

    }

    [TestMethod]
    public async Task Close_session_by_client_disconnect()
    {
        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token, packetCapture: new TestNullPacketCapture());
        Assert.IsTrue(accessManager.SessionController.Sessions.TryGetValue(client.SessionId, out var session));
        await client.DisposeAsync();

        await TestHelper.WaitForClientState(client, ClientState.Disposed);
        await VhTestUtil.AssertEqualsWait(false, () => session.IsAlive);
    }

    [TestMethod]
    public async Task Restore_session_after_restarting_server()
    {
        // create server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        using var accessManager1 = TestHelper.CreateAccessManager(fileAccessManagerOptions);
        await using var server1 = await TestHelper.CreateServer(accessManager1);

        // create client
        var token = TestHelper.CreateAccessToken(server1);
        await using var client = await TestHelper.CreateClient(token);
        Assert.AreEqual(ClientState.Connected, client.State);
        await TestHelper.Test_Https();

        // restart server and access manager
        await server1.DisposeAsync();
        accessManager1.Dispose();
        accessManager1.Dispose();
        using var accessManager2 = TestHelper.CreateAccessManager(fileAccessManagerOptions, accessManager1.StoragePath);
        await using var server2 = await TestHelper.CreateServer(accessManager2);

        VhLogger.Instance.LogInformation("Test: Sending another HTTP Request...");
        await TestHelper.Test_Https();
        Assert.AreEqual(ClientState.Connected, client.State);
        await client.DisposeAsync(); //dispose before server2
    }

    [TestMethod]
    public async Task Recover_should_call_access_server_only_once()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // Create Client
        var token1 = TestHelper.CreateAccessToken(accessManager);
        await using var client = await TestHelper.CreateClient(token1);

        await server.DisposeAsync();
        await using var server2 = await TestHelper.CreateServer(accessManager);
        await Task.WhenAll(
            TestHelper.Test_Https(timeout: 10000, throwError: false),
            TestHelper.Test_Https(timeout: 10000, throwError: false),
            TestHelper.Test_Https(timeout: 10000, throwError: false),
            TestHelper.Test_Https(timeout: 10000, throwError: false)
        );

        Assert.AreEqual(1, accessManager.SessionGetCounter);


        await client.DisposeAsync();
        await server2.DisposeAsync();
    }

    [TestMethod]
    public async Task Unauthorized_response_is_expected_for_unknown_request()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        var client = new HttpClient(handler);
        var url = $"https://{token.ServerToken.HostEndPoints!.First()}";

        var ex = await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.GetStringAsync(url));
        Assert.AreEqual(ex.StatusCode, HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Server_should_close_session_if_it_does_not_exist_in_access_server()
    {
        // create server
        var accessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        accessManagerOptions.SessionOptions.SyncCacheSize = 1000000;
        using var accessManager = TestHelper.CreateAccessManager(accessManagerOptions);
        await using var server = await TestHelper.CreateServer(accessManager);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token);

        accessManager.SessionController.Sessions.Clear();
        await server.SessionManager.SyncSessions();

        await VhTestUtil.AssertEqualsWait(ClientState.Disposed, async () =>
        {
            await TestHelper.Test_Https(throwError: false, timeout: 1000);
            return client.State;
        });
        Assert.AreEqual(ClientState.Disposed, client.State);
        Assert.AreEqual(SessionErrorCode.AccessError, client.SessionStatus.ErrorCode);
    }

    [TestMethod]
    public void Merge_config()
    {
        var oldServerConfig = new ServerConfig();
        var newServerConfig = new ServerConfig
        {
            LogAnonymizer = true,
            MaxCompletionPortThreads = 10,
            MinCompletionPortThreads = 11,
            UpdateStatusInterval = TimeSpan.FromHours(11),
            NetFilterOptions = new NetFilterOptions
            {
                BlockIpV6 = true,
                ExcludeIpRanges = [IpRange.Parse("1.1.1.1-1.1.1.2")],
                IncludeIpRanges = [IpRange.Parse("1.1.1.1-1.1.1.3")],
                PacketCaptureExcludeIpRanges = [IpRange.Parse("1.1.1.1-1.1.1.4")],
                PacketCaptureIncludeIpRanges = [IpRange.Parse("1.1.1.1-1.1.1.5")],
                IncludeLocalNetwork = false
            },
            TcpEndPoints = [IPEndPoint.Parse("2.2.2.2:4433")],
            UdpEndPoints = [IPEndPoint.Parse("3.3.3.3:5533")],
            TrackingOptions = new TrackingOptions
            {
                TrackClientIp = true,
                TrackDestinationIp = false,
                TrackDestinationPort = true,
                TrackIcmp = false,
                TrackLocalPort = true,
                TrackTcp = false,
                TrackUdp = true
            },
            SessionOptions = new SessionOptions
            {
                IcmpTimeout = TimeSpan.FromMinutes(50),
                MaxDatagramChannelCount = 13,
                MaxTcpChannelCount = 14,
                MaxTcpConnectWaitCount = 16,
                MaxUdpClientCount = 17,
                NetScanLimit = 18,
                NetScanTimeout = TimeSpan.FromMinutes(51),
                SyncCacheSize = 19,
                SyncInterval = TimeSpan.FromMinutes(52),
                TcpBufferSize = 20,
                TcpConnectTimeout = TimeSpan.FromMinutes(53),
                TcpTimeout = TimeSpan.FromMinutes(54),
                Timeout = TimeSpan.FromMinutes(55),
                UdpTimeout = TimeSpan.FromMinutes(56),
                UseUdpProxy2 = true
            }
        };

        oldServerConfig.Merge(newServerConfig);
        Assert.AreEqual(JsonSerializer.Serialize(oldServerConfig), JsonSerializer.Serialize(newServerConfig));
    }

    [TestMethod]
    public async Task DnsChallenge()
    {
        VhLogger.IsAnonymousMode = true;

        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.ServerConfig.UpdateStatusInterval = TimeSpan.FromMilliseconds(300);
        await using var server = await TestHelper.CreateServer(accessManager);

        // set DnsChallenge
        var dnsChallenge = new DnsChallenge
        {
            KeyAuthorization = "DnsChallenge_KeyAuthorization",
            Token = "DnsChallenge",
            Timeout = TimeSpan.FromSeconds(60)
        };
        accessManager.ServerConfig.DnsChallenge = dnsChallenge;

        // notify server
        accessManager.ServerConfig.ConfigCode = Guid.NewGuid().ToString();
        await VhTestUtil.AssertEqualsWait(accessManager.ServerConfig.ConfigCode,
            () => accessManager.LastServerStatus!.ConfigCode);

        // server should listen to port 80 for HTTP-01 challenge
        var httpClient = new HttpClient();
        var url = new Uri($"http://{accessManager.ServerConfig.TcpEndPointsValue[0].Address}:80/.well-known/acme-challenge/{dnsChallenge.Token}");
        var keyAuthorization = await httpClient.GetStringAsync(url);
        Assert.AreEqual(dnsChallenge.KeyAuthorization, keyAuthorization);

        // check invalid url
        url = new Uri($"http://{accessManager.ServerConfig.TcpEndPointsValue[0].Address}:80/.well-known/acme-challenge/{Guid.NewGuid()}");
        var response = await httpClient.GetAsync(url);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);

        // remove challenge and notify server
        accessManager.ServerConfig.DnsChallenge = null;
        accessManager.ServerConfig.ConfigCode = Guid.NewGuid().ToString();
        await VhTestUtil.AssertEqualsWait(accessManager.ServerConfig.ConfigCode,
            () => accessManager.LastServerStatus!.ConfigCode);

        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => httpClient.GetAsync(url));

    }
}