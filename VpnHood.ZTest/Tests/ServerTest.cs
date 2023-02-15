using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Server.Configurations;
using VpnHood.Server.Providers.FileAccessServerProvider;

namespace VpnHood.Test.Tests;

[TestClass]
public class ServerTest
{
    [TestInitialize]
    public void Initialize()
    {
        VhLogger.Instance = VhLogger.CreateConsoleLogger(true);
    }

    [TestMethod]
    public async Task Auto_sync_sessions_by_interval()
    {
        // Create Server
        var serverOptions = TestHelper.CreateFileAccessServerOptions();
        serverOptions.SessionOptions.SyncCacheSize = 10000000;
        serverOptions.SessionOptions.SyncInterval = TimeSpan.FromMicroseconds(200);
        var fileAccessServer = TestHelper.CreateFileAccessServer(serverOptions);
        using var testAccessServer = new TestAccessServer(fileAccessServer);
        await using var server = TestHelper.CreateServer(testAccessServer);

        // Create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = TestHelper.CreateClient(token, options: new ClientOptions { UseUdpChannel = true });

        // check usage when usage should be 0
        var sessionResponseEx = await testAccessServer.Session_Get(client.SessionId, client.HostEndPoint!, null);
        Assert.IsTrue(sessionResponseEx.AccessUsage!.ReceivedTraffic == 0);

        // lets do transfer
        await TestHelper.Test_HttpsAsync();

        // check usage should still not be 0 after interval
        await Task.Delay(1000);
        sessionResponseEx = await testAccessServer.Session_Get(client.SessionId, client.HostEndPoint!, null);
        Assert.IsTrue(sessionResponseEx.AccessUsage!.ReceivedTraffic > 0);
    }

    [TestMethod]
    public async Task Reconfigure()
    {
        var serverEndPoint = Util.GetFreeTcpEndPoint(IPAddress.Loopback);
        var fileAccessServerOptions = new FileAccessServerOptions { TcpEndPoints = new[] { serverEndPoint } };
        using var fileAccessServer = TestHelper.CreateFileAccessServer(fileAccessServerOptions);
        var serverConfig = fileAccessServer.ServerConfig;
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
        using var testAccessServer = new TestAccessServer(fileAccessServer);

        var dateTime = DateTime.Now;
        await using var server = TestHelper.CreateServer(testAccessServer);
        Assert.IsTrue(testAccessServer.LastConfigureTime > dateTime);

        dateTime = DateTime.Now;
        fileAccessServer.ServerConfig.ConfigCode = Guid.NewGuid().ToString();
        for (var i = 0; i < 30 && fileAccessServer.ServerConfig.ConfigCode != testAccessServer.LastServerStatus!.ConfigCode; i++)
            await Task.Delay(100);

        Assert.AreEqual(fileAccessServer.ServerConfig.ConfigCode, testAccessServer.LastServerStatus!.ConfigCode);
        Assert.IsTrue(testAccessServer.LastConfigureTime > dateTime);
        Assert.IsTrue(server.SessionManager.TrackingOptions.TrackClientIp);
        Assert.IsTrue(server.SessionManager.TrackingOptions.TrackLocalPort);
        Assert.AreEqual(serverConfig.TrackingOptions.TrackClientIp, server.SessionManager.TrackingOptions.TrackClientIp);
        Assert.AreEqual(serverConfig.TrackingOptions.TrackLocalPort, server.SessionManager.TrackingOptions.TrackLocalPort);
        Assert.AreEqual(serverConfig.SessionOptions.TcpTimeout, server.SessionManager.SessionOptions.TcpTimeout);
        Assert.AreEqual(serverConfig.SessionOptions.IcmpTimeout, server.SessionManager.SessionOptions.IcmpTimeout);
        Assert.AreEqual(serverConfig.SessionOptions.UdpTimeout, server.SessionManager.SessionOptions.UdpTimeout);
        Assert.AreEqual(serverConfig.SessionOptions.Timeout, server.SessionManager.SessionOptions.Timeout);
        Assert.AreEqual(serverConfig.SessionOptions.MaxDatagramChannelCount, server.SessionManager.SessionOptions.MaxDatagramChannelCount);
        Assert.AreEqual(serverConfig.SessionOptions.SyncCacheSize, server.SessionManager.SessionOptions.SyncCacheSize);
        Assert.AreEqual(serverConfig.SessionOptions.TcpBufferSize, server.SessionManager.SessionOptions.TcpBufferSize);
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
        await TestHelper.WaitForClientStateAsync(client, ClientState.Disposed);
        Thread.Sleep(1000);

        Assert.IsFalse(session.IsAlive);
    }

    [TestMethod]
    public void Recover_closed_session_from_access_server()
    {
        // create server
        using var fileAccessServer = TestHelper.CreateFileAccessServer();
        using var testAccessServer = new TestAccessServer(fileAccessServer);
        using var server = TestHelper.CreateServer(testAccessServer);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        using var client = TestHelper.CreateClient(token);
        Assert.AreEqual(ClientState.Connected, client.State);
        TestHelper.Test_Https();

        // restart server
        server.Dispose();

        using var server2 = TestHelper.CreateServer(testAccessServer);
        TestHelper.Test_Https();
        Assert.AreEqual(ClientState.Connected, client.State);
    }

    [TestMethod]
    public async Task Recover_should_call_access_server_only_once()
    {
        using var fileAccessServer = TestHelper.CreateFileAccessServer();
        using var testAccessServer = new TestAccessServer(fileAccessServer);
        await using var server = TestHelper.CreateServer(testAccessServer);

        // Create Client
        var token1 = TestHelper.CreateAccessToken(fileAccessServer);
        await using var client = TestHelper.CreateClient(token1);

        await server.DisposeAsync();
        await using var server2 = TestHelper.CreateServer(testAccessServer);
        await Task.WhenAll(
            TestHelper.Test_HttpsAsync(timeout: 10000),
            TestHelper.Test_HttpsAsync(timeout: 10000),
            TestHelper.Test_HttpsAsync(timeout: 10000),
            TestHelper.Test_HttpsAsync(timeout: 10000)
        );

        Assert.AreEqual(1, testAccessServer.SessionGetCounter);
    }

    [TestMethod]
    public async Task Server_should_close_session_if_it_does_not_exist_in_access_server()
    {
        // create server
        var accessServerOptions = TestHelper.CreateFileAccessServerOptions();
        accessServerOptions.SessionOptions.SyncCacheSize = 1000000;
        using var fileAccessServer = TestHelper.CreateFileAccessServer(accessServerOptions);
        using var testAccessServer = new TestAccessServer(fileAccessServer);
        await using var server = TestHelper.CreateServer(testAccessServer);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = TestHelper.CreateClient(token);

        fileAccessServer.SessionManager.Sessions.Clear();
        await server.SessionManager.SyncSessions();

        await TestHelper.AssertEqualsWait(ClientState.Disposed, async () =>
        {
            await TestHelper.Test_HttpsAsync(throwError: false);
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
            AllowIpV6 = true,
            LogAnonymizer = true,
            MaxCompletionPortThreads = 10,
            MinCompletionPortThreads = 11,
            UpdateStatusInterval = TimeSpan.FromHours(11),
            NetFilterOptions = new NetFilterOptions
            {
                ExcludeIpRanges = new[] { IpRange.Parse("1.1.1.1-1.1.1.2") },
                IncludeIpRanges = new[] { IpRange.Parse("1.1.1.1-1.1.1.3") },
                PacketCaptureExcludeIpRanges = new[] { IpRange.Parse("1.1.1.1-1.1.1.4") },
                PacketCaptureIncludeIpRanges = new[] { IpRange.Parse("1.1.1.1-1.1.1.5") },
                ExcludeLocalNetwork = false,
            },
            TcpEndPoints = new[] { IPEndPoint.Parse("2.2.2.2:4433") },
            TrackingOptions = new TrackingOptions
            {
                TrackClientIp = true,
                TrackDestinationIp = false,
                TrackDestinationPort = true,
                TrackIcmp = false,
                TrackLocalPort = true,
                TrackTcp = false,
                TrackUdp = true,
            },
            SessionOptions = new SessionOptions
            {
                IcmpTimeout = TimeSpan.FromMinutes(50),
                MaxDatagramChannelCount = 13,
                MaxTcpChannelCount = 14,
                MaxTcpConnectWaitCount = 16,
                MaxUdpPortCount = 17,
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

}