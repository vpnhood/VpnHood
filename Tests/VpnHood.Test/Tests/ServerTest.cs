﻿using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Test.Device;
using VpnHood.Test.Providers;

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
            accessManager.LastServerInfo.PrivateIpAddresses.All(x => x.IsV4()) ||
            accessManager.LastServerInfo?.FreeUdpPortV6 > 0);
    }

    [TestMethod]
    public async Task Add_IpAddress_ToSystem()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.ServerConfig.AddListenerIpsToNetwork = "*";

        var netConfigurationProvider = new TestNetConfigurationProvider();
        await using var server =
            await TestHelper.CreateServer(accessManager, netConfigurationProvider: netConfigurationProvider);

        CollectionAssert.AreEqual(await netConfigurationProvider.GetInterfaceNames(),
            accessManager.LastServerInfo?.NetworkInterfaceNames);
        Assert.AreEqual(1, netConfigurationProvider.IpAddresses.Count);
        Assert.AreEqual(netConfigurationProvider.IpAddresses.Single().Key,
            accessManager.ServerConfig.TcpEndPointsValue.First().Address);
        Assert.AreEqual(netConfigurationProvider.IpAddresses.Single().Value,
            (await netConfigurationProvider.GetInterfaceNames()).First());

        await server.DisposeAsync();
        Assert.AreEqual(0, netConfigurationProvider.IpAddresses.Count);
    }


    [TestMethod]
    public async Task Auto_sync_sessions_by_interval()
    {
        // Create Server
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.SessionOptions.SyncCacheSize = 10;
        serverOptions.SessionOptions.SyncInterval = TimeSpan.FromMilliseconds(200);
        serverOptions.UpdateStatusInterval = TimeSpan.FromMilliseconds(200);
        var accessManager = TestHelper.CreateAccessManager(serverOptions);
        await using var server = await TestHelper.CreateServer(accessManager);

        // Create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client =
            await TestHelper.CreateClient(clientOptions: TestHelper.CreateClientOptions(token, useUdpChannel: true));

        // check usage when usage should be 0
        var sessionResponseEx = await accessManager.Session_Get(client.SessionId, client.HostTcpEndPoint!, null);
        Assert.IsTrue(sessionResponseEx.AccessUsage!.CycleTraffic.Received == 0);

        // lets do transfer
        await TestHelper.Test_Https();

        // check usage should still not be 0 after interval
        await VhTestUtil.AssertEqualsWait(true, async () => {
            sessionResponseEx = await accessManager.Session_Get(client.SessionId, client.HostTcpEndPoint!, null);
            return sessionResponseEx.AccessUsage!.CycleTraffic.Received > 0;
        });
    }

    [TestMethod]
    public async Task Reconfigure_Listeners()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.ServerConfig.UpdateStatusInterval = TimeSpan.FromMilliseconds(300);
        await using var server = await TestHelper.CreateServer(accessManager);

        // change tcp end points
        var newTcpEndPoint = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);
        VhLogger.Instance.LogDebug(GeneralEventId.Test,
            "Test: Changing access server TcpEndPoint. TcpEndPoint: {TcpEndPoint}", newTcpEndPoint);
        accessManager.ServerConfig.TcpEndPoints = [newTcpEndPoint];
        accessManager.ServerConfig.ConfigCode = Guid.NewGuid().ToString();
        await VhTestUtil.AssertEqualsWait(accessManager.ServerConfig.ConfigCode,
            () => accessManager.LastServerStatus!.ConfigCode);
        Assert.AreNotEqual(
            VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback, accessManager.ServerConfig.TcpEndPoints[0].Port),
            accessManager.ServerConfig.TcpEndPoints[0]);

        // change udp end points
        var newUdpEndPoint = VhUtils.GetFreeUdpEndPoint(IPAddress.Loopback);
        VhLogger.Instance.LogDebug(GeneralEventId.Test,
            "Test: Changing access server UdpEndPoint. UdpEndPoint: {UdpEndPoint}", newUdpEndPoint);
        accessManager.ServerConfig.UdpEndPoints = [newUdpEndPoint];
        accessManager.ServerConfig.ConfigCode = Guid.NewGuid().ToString();
        await VhTestUtil.AssertEqualsWait(accessManager.ServerConfig.ConfigCode,
            () => accessManager.LastServerStatus!.ConfigCode);

        Assert.AreNotEqual(
            VhUtils.GetFreeUdpEndPoint(IPAddress.Loopback, accessManager.ServerConfig.UdpEndPoints[0].Port),
            accessManager.ServerConfig.UdpEndPoints[0]);
    }

    [TestMethod]
    public async Task Reconfigure()
    {
        var serverEndPoint = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);
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
        serverConfig.SessionOptions.MaxPacketChannelCount = 2074;
        serverConfig.SessionOptions.SyncCacheSize = 2075;
        serverConfig.SessionOptions.StreamProxyBufferSize = new TransferBufferSize(2076, 2077);
        serverConfig.SessionOptions.UdpProxyBufferSize = new TransferBufferSize(4001, 4002);
        serverConfig.SessionOptions.UdpChannelBufferSize = new TransferBufferSize(5001, 5002);
        serverConfig.ServerSecret = VhUtils.GenerateKey();

        var dateTime = DateTime.Now;
        await using var server = await TestHelper.CreateServer(accessManager);
        Assert.IsTrue(accessManager.LastConfigureTime > dateTime);
        Assert.IsTrue(accessManager.LastServerInfo?.IsRestarted);

        dateTime = DateTime.Now;
        accessManager.ServerConfig.ConfigCode = Guid.NewGuid().ToString();
        await VhTestUtil.AssertEqualsWait(accessManager.ServerConfig.ConfigCode,
            () => accessManager.LastServerStatus!.ConfigCode);

        CollectionAssert.AreEqual(serverConfig.ServerSecret, server.SessionManager.ServerSecret);
        Assert.IsTrue(accessManager.LastConfigureTime > dateTime);
        Assert.IsTrue(server.SessionManager.TrackingOptions.TrackClientIp);
        Assert.IsTrue(server.SessionManager.TrackingOptions.TrackLocalPort);
        Assert.AreEqual(serverConfig.TrackingOptions.TrackClientIp, server.SessionManager.TrackingOptions.TrackClientIp);
        Assert.AreEqual(serverConfig.TrackingOptions.TrackLocalPort, server.SessionManager.TrackingOptions.TrackLocalPort);
        Assert.AreEqual(serverConfig.SessionOptions.TcpTimeout, server.SessionManager.SessionOptions.TcpTimeout);
        Assert.AreEqual(serverConfig.SessionOptions.IcmpTimeout, server.SessionManager.SessionOptions.IcmpTimeout);
        Assert.AreEqual(serverConfig.SessionOptions.UdpTimeout, server.SessionManager.SessionOptions.UdpTimeout);
        Assert.AreEqual(serverConfig.SessionOptions.Timeout, server.SessionManager.SessionOptions.Timeout);
        Assert.AreEqual(serverConfig.SessionOptions.MaxPacketChannelCount, server.SessionManager.SessionOptions.MaxPacketChannelCount);
        Assert.AreEqual(serverConfig.SessionOptions.SyncCacheSize, server.SessionManager.SessionOptions.SyncCacheSize);
        Assert.AreEqual(serverConfig.SessionOptions.StreamProxyBufferSize, server.SessionManager.SessionOptions.StreamProxyBufferSize);
        Assert.AreEqual(serverConfig.SessionOptions.UdpProxyBufferSize, server.SessionManager.SessionOptions.UdpProxyBufferSize);
        Assert.AreEqual(serverConfig.SessionOptions.UdpChannelBufferSize, server.SessionManager.SessionOptions.UdpChannelBufferSize);
        Assert.IsFalse(accessManager.LastServerInfo?.IsRestarted);
    }

    [TestMethod]
    public async Task Close_session_by_client_disconnect()
    {
        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token, vpnAdapter: new TestNullVpnAdapter());
        Assert.IsTrue(accessManager.SessionService.Sessions.TryGetValue(client.SessionId, out var session));
        await client.DisposeAsync();

        await client.WaitForState( ClientState.Disposed);
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
            TestHelper.Test_Https(timeout: TimeSpan.FromSeconds(10), throwError: false),
            TestHelper.Test_Https(timeout: TimeSpan.FromSeconds(10), throwError: false),
            TestHelper.Test_Https(timeout: TimeSpan.FromSeconds(10), throwError: false),
            TestHelper.Test_Https(timeout: TimeSpan.FromSeconds(10), throwError: false)
        );

        Assert.AreEqual(0, accessManager.SessionGetCounter, "session must be loaded in startup.");

        // remove session from access server
        server2.SessionManager.RemoveSession(server2.SessionManager.GetSessionById(client.SessionId)!);

        // try using recovery
        await Task.WhenAll(
            TestHelper.Test_Https(timeout: TimeSpan.FromSeconds(10), throwError: false),
            TestHelper.Test_Https(timeout: TimeSpan.FromSeconds(10), throwError: false),
            TestHelper.Test_Https(timeout: TimeSpan.FromSeconds(10), throwError: false),
            TestHelper.Test_Https(timeout: TimeSpan.FromSeconds(10), throwError: false)
        );

        Assert.AreEqual(1, accessManager.SessionGetCounter, "session must be recovered once.");
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

        var ex = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.GetStringAsync(url));
        Assert.AreEqual(ex.StatusCode, HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Server_should_close_session_if_it_does_not_exist_in_access_server()
    {
        // create server
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.SessionOptions.SyncCacheSize = 10;
        serverOptions.SessionOptions.SyncInterval = TimeSpan.FromMilliseconds(200);
        using var accessManager = TestHelper.CreateAccessManager(serverOptions);
        await using var server = await TestHelper.CreateServer(accessManager);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token);
        
        Log("Clearing all sessions...");
        accessManager.SessionService.Sessions.Clear();

        Log("Waiting for the client disposal...");
        await VhTestUtil.AssertEqualsWait(ClientState.Disposed, async () => {
            await TestHelper.Test_Https(throwError: false, timeout: TimeSpan.FromMilliseconds(100));
            return client.State;
        });
        Assert.AreEqual(SessionErrorCode.AccessError, client.GetLastSessionErrorCode());
    }

    [TestMethod]
    public void Merge_config()
    {
        var oldServerConfig = new ServerConfig();
        var newServerConfig = new ServerConfig {
            LogAnonymizer = true,
            MaxCompletionPortThreads = 10,
            MinCompletionPortThreads = 11,
            UpdateStatusInterval = TimeSpan.FromHours(11),
            NetFilterOptions = new NetFilterOptions {
                BlockIpV6 = true,
                ExcludeIpRanges = [IpRange.Parse("1.1.1.1-1.1.1.2")],
                IncludeIpRanges = [IpRange.Parse("1.1.1.1-1.1.1.3")],
                VpnAdapterExcludeIpRanges = [IpRange.Parse("1.1.1.1-1.1.1.4")],
                VpnAdapterIncludeIpRanges = [IpRange.Parse("1.1.1.1-1.1.1.5")],
                IncludeLocalNetwork = false
            },
            TcpEndPoints = [IPEndPoint.Parse("2.2.2.2:4433")],
            UdpEndPoints = [IPEndPoint.Parse("3.3.3.3:5533")],
            TrackingOptions = new TrackingOptions {
                TrackClientIp = true,
                TrackDestinationIp = false,
                TrackDestinationPort = true,
                TrackIcmp = false,
                TrackLocalPort = true,
                TrackTcp = false,
                TrackUdp = true
            },
            SessionOptions = new SessionOptions {
                IcmpTimeout = TimeSpan.FromMinutes(50),
                MaxPacketChannelCount = 13,
                MaxTcpChannelCount = 14,
                MaxTcpConnectWaitCount = 16,
                MaxUdpClientCount = 17,
                NetScanLimit = 18,
                NetScanTimeout = TimeSpan.FromMinutes(51),
                SyncCacheSize = 19,
                SyncInterval = TimeSpan.FromMinutes(52),
                StreamProxyBufferSize = new TransferBufferSize(9000, 90001),
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
        var dnsChallenge = new DnsChallenge {
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
        var url = new Uri(
            $"http://{accessManager.ServerConfig.TcpEndPointsValue[0].Address}:80/.well-known/acme-challenge/{dnsChallenge.Token}");
        var keyAuthorization = await httpClient.GetStringAsync(url);
        Assert.AreEqual(dnsChallenge.KeyAuthorization, keyAuthorization);

        // check invalid url
        url = new Uri(
            $"http://{accessManager.ServerConfig.TcpEndPointsValue[0].Address}:80/.well-known/acme-challenge/{Guid.NewGuid()}");
        var response = await httpClient.GetAsync(url);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);

        // remove challenge and notify server
        accessManager.ServerConfig.DnsChallenge = null;
        accessManager.ServerConfig.ConfigCode = Guid.NewGuid().ToString();
        await VhTestUtil.AssertEqualsWait(accessManager.ServerConfig.ConfigCode,
            () => accessManager.LastServerStatus!.ConfigCode);

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => httpClient.GetAsync(url));
    }

    [TestMethod]
    public async Task SwapMemory_Status()
    {
        // create server
        var swapMemoryProvider = new TestSwapMemoryProvider {
            AppSize = 100 * VhUtils.Megabytes,
            AppUsed = 10 * VhUtils.Megabytes,
            OtherSize = 200 * VhUtils.Megabytes,
            OtherUsed = 20 * VhUtils.Megabytes
        };

        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager,
            swapMemoryProvider: swapMemoryProvider);

        // check initial status. AppSize should be 0
        swapMemoryProvider.AppUsed = 0;
        await server.ConfigureAndSendStatus(CancellationToken.None);
        var serverStatus = accessManager.LastServerStatus;
        Assert.AreEqual(swapMemoryProvider.OtherSize, swapMemoryProvider.Info.TotalSize);
        Assert.AreEqual(0, swapMemoryProvider.Info.AppSize);
        Assert.AreEqual(swapMemoryProvider.OtherSize - swapMemoryProvider.OtherUsed, serverStatus?.AvailableSwapMemory);
        Assert.AreEqual(swapMemoryProvider.Info.TotalSize, serverStatus?.TotalSwapMemory);
        Assert.AreEqual(swapMemoryProvider.Info.TotalSize - swapMemoryProvider.Info.TotalUsed,
            serverStatus?.AvailableSwapMemory);

        // check status after setting app swap memory
        swapMemoryProvider.AppSize = 50 * VhUtils.Megabytes;
        swapMemoryProvider.AppUsed = 10 * VhUtils.Megabytes;

        await server.ConfigureAndSendStatus(CancellationToken.None);
        serverStatus = accessManager.LastServerStatus;
        Assert.AreEqual(swapMemoryProvider.Info.TotalSize, accessManager.LastServerStatus?.TotalSwapMemory);
        Assert.AreEqual(swapMemoryProvider.Info.TotalSize - swapMemoryProvider.Info.TotalUsed,
            serverStatus?.AvailableSwapMemory);

        // configure by access manager
        accessManager.ServerConfig.SwapMemorySizeMb = 2500;
        accessManager.ServerConfig.ConfigCode = Guid.NewGuid().ToString();
        swapMemoryProvider.AppUsed = 100 * VhUtils.Megabytes;
        await server.ConfigureAndSendStatus(CancellationToken.None);
        serverStatus = accessManager.LastServerStatus;
        Assert.AreEqual(swapMemoryProvider.Info.TotalSize, 2500 * VhUtils.Megabytes);
        Assert.AreEqual(swapMemoryProvider.Info.TotalSize, accessManager.LastServerStatus?.TotalSwapMemory);
        Assert.AreEqual(swapMemoryProvider.Info.TotalSize - swapMemoryProvider.Info.TotalUsed,
            serverStatus?.AvailableSwapMemory);
    }
}