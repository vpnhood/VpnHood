using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Configuration;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Test.Device;
using VpnHood.Test.Extensions;
using VpnHood.Test.Providers;
// ReSharper disable ShortLivedHttpClient

// ReSharper disable DisposeOnUsingVariable

namespace VpnHood.Test.Tests.Servers;

[TestClass]
public class ServerTest : TestBase
{
    [TestMethod]
    public async Task Configure()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        Assert.IsNotNull(accessManager.LastServerInfo);
        Assert.IsGreaterThan(0, accessManager.LastServerInfo.FreeUdpPortV4);
        Assert.IsTrue(
            accessManager.LastServerInfo.PrivateIpAddresses.All(x => x.IsV4()) ||
            accessManager.LastServerInfo?.FreeUdpPortV6 > 0);
    }

    [TestMethod]
    public async Task Configure_refuses_options_that_leave_tcp_no_way()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.ServerConfig.SessionOptions.AllowTcpPacket = false;
        accessManager.ServerConfig.SessionOptions.AllowTcpProxy = false;

        // autoStart: false, as the server never becomes ready
        await using var server = await TestHelper.CreateServer(accessManager, autoStart: false);
        await server.Start(TestCt);

        Assert.AreEqual(ServerState.Waiting, server.State);
        Assert.IsNotNull(accessManager.LastServerStatus?.ConfigError);
    }

    [TestMethod]
    public async Task Add_IpAddress_ToSystem()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.ServerConfig.AddListenerIpsToNetwork = "*";

        var netConfigurationProvider = new TestNetConfigurationProvider();
        await using var server =
            await TestHelper.CreateServer(accessManager, netConfigurationProvider: netConfigurationProvider);

        var interfaceNames = await netConfigurationProvider.GetInterfaceNames(TestCt);
        CollectionAssert.AreEqual(interfaceNames, accessManager.LastServerInfo?.NetworkInterfaceNames);
        Assert.HasCount(1, netConfigurationProvider.IpAddresses);
        Assert.AreEqual(netConfigurationProvider.IpAddresses.Single().Key,
            accessManager.ServerConfig.TcpEndPointsValue.First().Address);
        Assert.AreEqual(netConfigurationProvider.IpAddresses.Single().Value, interfaceNames.First());

        await server.DisposeAsync();
        Assert.IsEmpty(netConfigurationProvider.IpAddresses);
    }

    [TestMethod]
    public async Task A_server_host_close_past_its_limit_leaves_the_rest_of_the_close_to_run()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.ServerConfig.AddListenerIpsToNetwork = "*";
        var netConfigurationProvider = new TestNetConfigurationProvider();
        var quicServer = new QuicListenerCloseHoldingServer();
        var server = new VpnHoodServer(accessManager, new ServerOptions {
            SocketFactory = new TestSocketFactory(),
            QuicServer = quicServer,
            NetConfigurationProvider = netConfigurationProvider,
            StoragePath = TestHelper.WorkingPath,
            AutoDisposeAccessManager = false,
            PublicIpDiscovery = false,
            ServerHostDisposeTimeout = TimeSpan.FromSeconds(1)
        });
        await server.Start(TestCt);
        Assert.HasCount(1, server.ServerHost.QuicEndPoints);
        Assert.HasCount(1, netConfigurationProvider.IpAddresses);

        // the QUIC listener's close never ends on its own
        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        Assert.AreEqual(1, quicServer.HeldCloseCount);
        Assert.AreEqual(ServerState.Disposed, server.State);
        Assert.IsEmpty(netConfigurationProvider.IpAddresses);
        quicServer.ReleaseCloses();
    }

    [TestMethod]
    public async Task A_quick_close_ends_a_server_whose_graceful_close_is_held()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.ServerConfig.AddListenerIpsToNetwork = "*";
        var netConfigurationProvider = new TestNetConfigurationProvider();
        var quicServer = new QuicListenerCloseHoldingServer();
        var server = new VpnHoodServer(accessManager, new ServerOptions {
            SocketFactory = new TestSocketFactory(),
            QuicServer = quicServer,
            NetConfigurationProvider = netConfigurationProvider,
            StoragePath = TestHelper.WorkingPath,
            AutoDisposeAccessManager = false,
            PublicIpDiscovery = false,
            ServerHostDisposeTimeout = Timeout.InfiniteTimeSpan
        });
        await server.Start(TestCt);
        await using var client = await TestHelper.CreateClient(TestHelper.CreateAccessToken(server));
        var session = server.SessionManager.GetSessionById(client.SessionId);
        Assert.IsNotNull(session);

        // the graceful close waits for the QUIC listener's close, which is held
        var gracefulClose = server.DisposeAsync().AsTask();
        await VhTestUtil.AssertEqualsWait(1, () => Volatile.Read(ref quicServer.HeldCloseCount),
            "The graceful close should wait for the QUIC listener's close.", cancellationToken: TestCt);

        await Task.Run(server.Dispose, TestCt).WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.AreEqual(ServerState.Disposed, server.State);
        Assert.IsTrue(session.IsDisposed);
        Assert.IsEmpty(netConfigurationProvider.IpAddresses);

        // released, the graceful close finds the rest closed
        quicServer.ReleaseCloses();
        await gracefulClose.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
    }

    [TestMethod]
    public async Task A_graceful_close_after_a_quick_close_waits_for_the_hosts_close()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        var quicServer = new QuicListenerCloseHoldingServer();
        var server = new VpnHoodServer(accessManager, new ServerOptions {
            SocketFactory = new TestSocketFactory(),
            QuicServer = quicServer,
            StoragePath = TestHelper.WorkingPath,
            AutoDisposeAccessManager = false,
            PublicIpDiscovery = false,
            ServerHostDisposeTimeout = Timeout.InfiniteTimeSpan
        });
        await server.Start(TestCt);

        // the quick close leaves the host's close running, held at the QUIC listener
        server.Dispose();
        await VhTestUtil.AssertEqualsWait(1, () => Volatile.Read(ref quicServer.HeldCloseCount),
            "The quick close should start the host's close.", cancellationToken: TestCt);

        var gracefulClose = server.DisposeAsync().AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestCt);
        Assert.IsFalse(gracefulClose.IsCompleted, "The graceful close should wait for the host's close.");

        quicServer.ReleaseCloses();
        await gracefulClose.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
    }

    [TestMethod]
    public async Task The_host_closes_beside_a_status_upload_under_way()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        var quicServer = new QuicListenerCloseHoldingServer();
        var server = new VpnHoodServer(accessManager, new ServerOptions {
            SocketFactory = new TestSocketFactory(),
            QuicServer = quicServer,
            StoragePath = TestHelper.WorkingPath,
            AutoDisposeAccessManager = false,
            PublicIpDiscovery = false,
            ServerHostDisposeTimeout = Timeout.InfiniteTimeSpan
        });
        await server.Start(TestCt);

        // a status upload under way, held at the access manager
        accessManager.StatusUpdateHold.IsHeld = true;
        var statusUpload = server.ConfigureAndSendStatus(TestCt).AsTask();
        await VhTestUtil.AssertEqualsWait(1, () => accessManager.StatusUpdateHold.HeldCount,
            "The status upload should be under way.", cancellationToken: TestCt);

        var close = server.DisposeAsync().AsTask();
        await VhTestUtil.AssertEqualsWait(1, () => Volatile.Read(ref quicServer.HeldCloseCount),
            "The host's close should start beside the upload.", timeout: 3000, cancellationToken: TestCt);

        accessManager.StatusUpdateHold.Release();
        quicServer.ReleaseCloses();
        await Task.WhenAll(statusUpload, close).WaitAsync(TimeSpan.FromSeconds(10), TestCt);
    }

    [TestMethod]
    public async Task A_host_configuration_under_way_ends_before_the_hosts_close_and_none_starts_after()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        var quicServer = new QuicListenerCloseHoldingServer();
        quicServer.ReleaseCloses();
        quicServer.ListenHold.IsHeld = true;
        await using var server = new VpnHoodServer(accessManager, new ServerOptions {
            SocketFactory = new TestSocketFactory(),
            QuicServer = quicServer,
            StoragePath = TestHelper.WorkingPath,
            AutoDisposeAccessManager = false,
            PublicIpDiscovery = false
        });
        var configuration = new ServerHostConfiguration {
            TcpEndPoints = [IPEndPoint.Parse("127.0.0.1:0")],
            UdpEndPoints = [],
            QuicEndPoints = [IPEndPoint.Parse("127.0.0.1:443")],
            DnsServers = null,
            Certificates = [accessManager.DefaultCert],
            UdpChannelBufferSize = null
        };

        // a configuration under way, held in its listen
        var configure = server.ServerHost.Configure(configuration, TestCt);
        await VhTestUtil.AssertEqualsWait(1, () => quicServer.ListenHold.HeldCount,
            "The configuration should be listening.", cancellationToken: TestCt);

        // the close lets it end, and closes the listener it added
        var close = server.ServerHost.DisposeAsync().AsTask();
        quicServer.ListenHold.Release();
        await Task.WhenAll(configure, close).WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        Assert.AreEqual(1, Volatile.Read(ref quicServer.HeldCloseCount));

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => server.ServerHost.Configure(configuration, TestCt));
    }

    [TestMethod]
    public async Task A_configuration_the_close_interrupts_applies_nothing_and_records_no_error()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.ServerConfig.AddListenerIpsToNetwork = "*";
        accessManager.ConfigureHold.IsHeld = true;
        var netConfigurationProvider = new TestNetConfigurationProvider();
        var server = new VpnHoodServer(accessManager, new ServerOptions {
            SocketFactory = new TestSocketFactory(),
            NetConfigurationProvider = netConfigurationProvider,
            StoragePath = TestHelper.WorkingPath,
            AutoDisposeAccessManager = false,
            PublicIpDiscovery = false
        });

        // a configuration under way, held at the access manager, when the close begins
        var start = server.Start(TestCt);
        await VhTestUtil.AssertEqualsWait(1, () => accessManager.ConfigureHold.HeldCount,
            "The configuration should be under way.", cancellationToken: TestCt);
        var close = server.DisposeAsync().AsTask();

        // held and released at once, the adds are counted but not stopped
        netConfigurationProvider.AddHold.IsHeld = true;
        netConfigurationProvider.AddHold.Release();
        accessManager.ConfigureHold.Release();

        // it adds no IP, and records no strike, which would keep the next start from configuring
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => start);
        await close.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        Assert.AreEqual(0, netConfigurationProvider.AddHold.HeldCount);
        Assert.IsFalse(File.Exists(Path.Combine(TestHelper.WorkingPath, ConfigErrorTracker.StrikeFileName)));
    }

    [TestMethod]
    public async Task An_address_added_after_the_close_is_removed_at_once()
    {
        var netConfigurationProvider = new TestNetConfigurationProvider();
        netConfigurationProvider.AddHold.IsHeld = true;
        var netConfigurationService = new NetConfigurationService(netConfigurationProvider);

        // an add under way, held at the provider, when the close runs
        var add = netConfigurationService.AddIpAddress(IPAddress.Parse("10.10.0.1"), "eth0", TestCt);
        await VhTestUtil.AssertEqualsWait(1, () => netConfigurationProvider.AddHold.HeldCount,
            "The add should be under way.", cancellationToken: TestCt);
        netConfigurationService.Dispose();
        netConfigurationProvider.AddHold.Release();

        await add.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.IsEmpty(netConfigurationProvider.IpAddresses);
    }

    [TestMethod]
    public async Task A_second_close_of_the_sessions_waits_for_the_first()
    {
        // traffic that stays unreported until the close, so its last sync has usage to send
        var options = TestHelper.CreateFileAccessManagerOptions();
        options.SessionOptions.SyncCacheSize = 1_000_000_000;
        using var accessManager = TestHelper.CreateAccessManager(options);
        await using var server = await TestHelper.CreateServer(accessManager);
        await using var client = await TestHelper.CreateClient(TestHelper.CreateAccessToken(server));
        await TestHelper.Test_Https();

        // the first close's last sync, held at the access manager
        accessManager.UsageHold.IsHeld = true;
        var firstClose = server.SessionManager.DisposeAsync().AsTask();
        await VhTestUtil.AssertEqualsWait(1, () => accessManager.UsageHold.HeldCount,
            "The last sync should be under way.", cancellationToken: TestCt);

        var secondClose = server.SessionManager.DisposeAsync().AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestCt);
        Assert.IsFalse(secondClose.IsCompleted, "The second close should wait for the first.");

        accessManager.UsageHold.Release();
        await Task.WhenAll(firstClose, secondClose).WaitAsync(TimeSpan.FromSeconds(10), TestCt);
    }

    [TestMethod]
    public async Task A_quick_close_of_the_sessions_ends_their_last_sync_at_once()
    {
        // traffic that stays unreported until the close, so its last sync has usage to send
        var options = TestHelper.CreateFileAccessManagerOptions();
        options.SessionOptions.SyncCacheSize = 1_000_000_000;
        using var accessManager = TestHelper.CreateAccessManager(options);
        await using var server = await TestHelper.CreateServer(accessManager);
        await using var client = await TestHelper.CreateClient(TestHelper.CreateAccessToken(server));
        await TestHelper.Test_Https();

        // the graceful close's last sync, held at the access manager, which lets it wait for 7 s
        accessManager.UsageHold.IsHeld = true;
        var close = server.SessionManager.DisposeAsync().AsTask();
        await VhTestUtil.AssertEqualsWait(1, () => accessManager.UsageHold.HeldCount,
            "The last sync should be under way.", cancellationToken: TestCt);

        server.SessionManager.Dispose();
        await close.WaitAsync(TimeSpan.FromSeconds(2), TestCt);
        accessManager.UsageHold.Release();
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
            await TestHelper.CreateClient(clientOptions: TestHelper.CreateClientOptions(token,
                channelProtocol: ChannelProtocol.Udp));

        // check usage when usage should be 0
        var sessionResponseEx = await accessManager.Session_Get(
            client.SessionId, client.RequiredSession.Config.HostTcpEndPoint!, null, TestCt);
        Assert.AreEqual(0, sessionResponseEx.AccessUsage!.CycleTraffic.Received);

        // lets do transfer
        await TestHelper.Test_Https();

        // check usage should still not be 0 after interval
        await VhTestUtil.AssertEqualsWait(true, async () => {
            sessionResponseEx = await accessManager.Session_Get(
                client.SessionId, client.RequiredSession.Config.HostTcpEndPoint!, null, TestCt);
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
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
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
        Assert.AreEqual(serverConfig.TrackingOptions.TrackClientIp,
            server.SessionManager.TrackingOptions.TrackClientIp);
        Assert.AreEqual(serverConfig.TrackingOptions.TrackLocalPort,
            server.SessionManager.TrackingOptions.TrackLocalPort);
        Assert.AreEqual(serverConfig.SessionOptions.TcpTimeout, server.SessionManager.SessionOptions.TcpTimeout);
        Assert.AreEqual(serverConfig.SessionOptions.IcmpTimeout, server.SessionManager.SessionOptions.IcmpTimeout);
        Assert.AreEqual(serverConfig.SessionOptions.UdpTimeout, server.SessionManager.SessionOptions.UdpTimeout);
        Assert.AreEqual(serverConfig.SessionOptions.Timeout, server.SessionManager.SessionOptions.Timeout);
        Assert.AreEqual(serverConfig.SessionOptions.MaxPacketChannelCount,
            server.SessionManager.SessionOptions.MaxPacketChannelCount);
        Assert.AreEqual(serverConfig.SessionOptions.SyncCacheSize, server.SessionManager.SessionOptions.SyncCacheSize);
        Assert.AreEqual(serverConfig.SessionOptions.StreamProxyBufferSize,
            server.SessionManager.SessionOptions.StreamProxyBufferSize);
        Assert.AreEqual(serverConfig.SessionOptions.UdpProxyBufferSize,
            server.SessionManager.SessionOptions.UdpProxyBufferSize);
        Assert.AreEqual(serverConfig.SessionOptions.UdpChannelBufferSize,
            server.SessionManager.SessionOptions.UdpChannelBufferSize);
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

        await client.WaitForState(ClientState.Disposed);
        await VhTestUtil.AssertEqualsWait(false, () => session?.IsAlive);
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
    public async Task Request_during_an_idle_removal_recovers_the_session()
    {
        await using var server = await TestHelper.CreateServer();
        await using var client = await TestHelper.CreateClient(TestHelper.CreateAccessToken(server),
            vpnAdapter: new TestNullVpnAdapter());
        var removedSession = server.GetSession(client);

        // an idle removal has stopped the session, still open, and its IPs went back with it; it is still listed
        server.SessionManager.DisposeSession(removedSession);
        Assert.IsNull(server.SessionManager.GetSessionByVirtualIp(removedSession.VirtualIps.IpV4));

        // the client comes back now: the session is recovered, with its IPs, rather than reported closed
        await client.UpdateSessionStatus(TestCt);
        var recoveredSession = server.GetSession(client);
        Assert.AreNotSame(removedSession, recoveredSession);
        Assert.AreSame(recoveredSession, server.SessionManager.GetSessionByVirtualIp(removedSession.VirtualIps.IpV4));

        // the removal then finishes and leaves the recovered session alone
        server.SessionManager.RemoveSession(removedSession);
        Assert.AreSame(recoveredSession, server.GetSession(client));
        Assert.AreSame(recoveredSession, server.SessionManager.GetSessionByVirtualIp(removedSession.VirtualIps.IpV4));
    }

    [TestMethod]
    public async Task Virtual_ips_are_released_only_by_the_session_that_holds_them()
    {
        await using var server = await TestHelper.CreateServer();
        await using var client1 = await TestHelper.CreateClient(TestHelper.CreateAccessToken(server),
            vpnAdapter: new TestNullVpnAdapter());
        await using var client2 = await TestHelper.CreateClient(TestHelper.CreateAccessToken(server),
            vpnAdapter: new TestNullVpnAdapter());
        var session1 = server.GetSession(client1);
        var session2 = server.GetSession(client2);
        var virtualIpManager = new VirtualIpManager(IpNetwork.Parse("10.10.0.0/16"), IpNetwork.Parse("fd00::/112"),
            Path.Combine(TestHelper.WorkingPath, "last_virtual_ips.json"));
        var virtualIps = virtualIpManager.Allocate();

        // a session keeps its IPs until it releases them
        virtualIpManager.Add(virtualIps, session1);
        Assert.ThrowsExactly<SessionException>(() => virtualIpManager.Add(virtualIps, session2));
        virtualIpManager.Release(virtualIps, session1);
        Assert.IsNull(virtualIpManager.FindSession(virtualIps.IpV4));

        // once another session holds them, a late release by the first leaves them alone
        virtualIpManager.Add(virtualIps, session2);
        virtualIpManager.Release(virtualIps, session1);
        Assert.AreSame(session2, virtualIpManager.FindSession(virtualIps.IpV4));
        Assert.AreSame(session2, virtualIpManager.FindSession(virtualIps.IpV6));

        // a failed add leaves nothing behind: an IPv4 taken before its IPv6 is refused goes back
        var conflictingIps = new VirtualIpBundle { IpV4 = virtualIpManager.Allocate().IpV4, IpV6 = virtualIps.IpV6 };
        Assert.ThrowsExactly<SessionException>(() => virtualIpManager.Add(conflictingIps, session1));
        Assert.IsNull(virtualIpManager.FindSession(conflictingIps.IpV4));
    }

    [TestMethod]
    public async Task Old_session_records_go_unless_their_session_is_in_memory()
    {
        await using var server = await TestHelper.CreateServer();
        await using var client1 = await TestHelper.CreateClient(TestHelper.CreateAccessToken(server),
            vpnAdapter: new TestNullVpnAdapter());
        await using var client2 = await TestHelper.CreateClient(TestHelper.CreateAccessToken(server),
            vpnAdapter: new TestNullVpnAdapter());
        var liveSession = server.GetSession(client1);
        var goneSession = server.GetSession(client2);

        // only the first session is in memory; both records were last written 8 days ago
        var storagePath = Path.Combine(TestHelper.WorkingPath, "session-records");
        var liveRecordPath = Path.Combine(storagePath, $"{liveSession.SessionId}.session");
        var goneRecordPath = Path.Combine(storagePath, $"{goneSession.SessionId}.session");
        using var sessionLocalService = new SessionLocalService(storagePath,
            new Dictionary<ulong, Session> { [liveSession.SessionId] = liveSession });
        sessionLocalService.Update(liveSession);
        sessionLocalService.Update(goneSession);
        var oldTime = DateTime.UtcNow.AddDays(-8);
        File.SetLastWriteTimeUtc(liveRecordPath, oldTime);
        File.SetLastWriteTimeUtc(goneRecordPath, oldTime);
        await sessionLocalService.CleanupSessionFiles(TestCt);

        // the live session keeps its record, refreshed, so a restart can recover it; the other's is gone
        Assert.IsGreaterThan(oldTime.AddDays(7), File.GetLastWriteTimeUtc(liveRecordPath));
        Assert.IsFalse(File.Exists(goneRecordPath));

        // a record written lately stays, though its session is not in memory: its client may come back
        sessionLocalService.Update(goneSession);
        await sessionLocalService.CleanupSessionFiles(TestCt);
        Assert.IsTrue(File.Exists(goneRecordPath));
    }

    [TestMethod]
    public void Sessions_created_at_once_get_different_virtual_ips()
    {
        Directory.CreateDirectory(TestHelper.WorkingPath);
        var virtualIpManager = new VirtualIpManager(IpNetwork.Parse("10.10.0.0/16"), IpNetwork.Parse("fd00::/112"),
            Path.Combine(TestHelper.WorkingPath, "last_virtual_ips.json"));

        // the first session has not added its IPs yet when the second one allocates
        var virtualIps1 = virtualIpManager.Allocate();
        var virtualIps2 = virtualIpManager.Allocate();
        Assert.AreNotEqual(virtualIps1.IpV4, virtualIps2.IpV4);
        Assert.AreNotEqual(virtualIps1.IpV6, virtualIps2.IpV6);
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
        Assert.AreEqual(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    // written by hand, with our version header, as a scan that knows it sends them
    [TestMethod]
    [DataRow("GET", "X-BinaryStream: None\r\n")]
    [DataRow("POST", "Upgrade: websocket\r\nConnection: Upgrade\r\nContent-Length: 0\r\n")]
    [DataRow("GET", "X-BinaryStream: WebSocketNoHandshake\r\n")]
    public async Task A_request_no_client_of_ours_sends_gets_the_401_of_a_scan(string method, string headers)
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        var request =
            $"{method} /{Guid.NewGuid()} HTTP/1.1\r\n" +
            $"Host: {token.ServerToken.HostName}\r\n" +
            $"X-ProtocolVersion: {VpnHoodClientConfig.MaxProtocolVersion}\r\n" +
            headers +
            "\r\n";
        Assert.AreEqual(HttpStatusCode.Unauthorized, await SendRawRequest(token, request));
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
        Assert.AreEqual(SessionErrorCode.AccessError, client.GetSessionErrorCode());
    }

    [TestMethod]
    [DataRow(null, ServerHost.MinProtocolVersion)]
    [DataRow(ServerHost.MinProtocolVersion - 1, ServerHost.MinProtocolVersion)]
    [DataRow(1000, 1000)]
    public async Task The_setting_only_raises_the_servers_own_minimum_protocol(int? minClientProtocolVersion,
        int expectedMinClientProtocolVersion)
    {
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.MinClientProtocolVersion = minClientProtocolVersion;
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);
        Assert.AreEqual(expectedMinClientProtocolVersion, server.ServerHost.MinClientProtocolVersion);
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
            ReverseProxyProviders = [ReverseProxyProvider.CloudFront],
            ReverseProxyIpRanges = [IpRange.Parse("4.4.4.0-4.4.4.255")],
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
                MinClientProtocolVersion = 57
            }
        };

        oldServerConfig.Merge(newServerConfig);
        Assert.AreEqual(JsonSerializer.Serialize(oldServerConfig), JsonSerializer.Serialize(newServerConfig));
    }

    [TestMethod]
    [DoNotParallelize] // listens on the fixed well-known port 80
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task AcmeHttp01Challenge(bool absoluteForm, bool query)
    {
        VhLogger.IsAnonymousMode = true;

        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.AcmeHttp01KeyToken = Guid.NewGuid().ToString();
        accessManager.AcmeHttp01KeyAuthorization = Guid.NewGuid().ToString();
        accessManager.ServerConfig.UpdateStatusInterval = TimeSpan.FromMilliseconds(300);
        accessManager.ServerConfig.EnableHttp01Challenge = true;
        accessManager.ServerConfig.TcpEndPoints = [VhUtils.GetFreeTcpEndPoint(IPAddress.Parse("127.90.91.92"))];
        await using var server = await TestHelper.CreateServer(accessManager);

        // server should listen to port 80 for HTTP-01 challenge
        using var httpClient = new HttpClient();
        var address = accessManager.ServerConfig.TcpEndPointsValue[0].Address;
        var path = $"/.well-known/acme-challenge/{accessManager.AcmeHttp01KeyToken}" + (query ? "?x=1" : "");
        var target = absoluteForm ? $"http://{address}{path}" : path;
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(address, 80, TestCt);
        var stream = tcpClient.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\nHost: {address}\r\n\r\n"), TestCt);
        using var reader = new StreamReader(stream);
        var response = await reader.ReadToEndAsync(TestCt);
        StringAssert.StartsWith(response, "HTTP/1.1 200 ");
        StringAssert.EndsWith(response, accessManager.AcmeHttp01KeyAuthorization);
    }

    // as anything but a challenge: what HTTPS redirection answers, to the server's TLS port
    [TestMethod]
    [DoNotParallelize] // listens on the fixed well-known port 80
    public async Task Http01Challenge_redirects_a_token_it_doesnt_know_to_https()
    {
        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.AcmeHttp01KeyToken = Guid.NewGuid().ToString();
        accessManager.AcmeHttp01KeyAuthorization = Guid.NewGuid().ToString();
        accessManager.ServerConfig.UpdateStatusInterval = TimeSpan.FromMilliseconds(300);
        accessManager.ServerConfig.EnableHttp01Challenge = true;
        accessManager.ServerConfig.TcpEndPoints = [VhUtils.GetFreeTcpEndPoint(IPAddress.Parse("127.90.91.92"))];
        await using var server = await TestHelper.CreateServer(accessManager);

        using var httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var tlsEndPoint = accessManager.ServerConfig.TcpEndPointsValue[0];
        var path = $"/.well-known/acme-challenge/{Guid.NewGuid()}";
        var response = await httpClient.GetAsync(new Uri($"http://{tlsEndPoint.Address}:80{path}"), TestCt);
        Assert.AreEqual(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.AreEqual(new Uri($"https://{tlsEndPoint}{path}"), response.Headers.Location);
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
        await server.ConfigureAndSendStatus(TestCt);
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

        await server.ConfigureAndSendStatus(TestCt);
        serverStatus = accessManager.LastServerStatus;
        Assert.AreEqual(swapMemoryProvider.Info.TotalSize, accessManager.LastServerStatus?.TotalSwapMemory);
        Assert.AreEqual(swapMemoryProvider.Info.TotalSize - swapMemoryProvider.Info.TotalUsed,
            serverStatus?.AvailableSwapMemory);

        // configure by access manager
        accessManager.ServerConfig.SwapMemorySizeMb = 2500;
        accessManager.ServerConfig.ConfigCode = Guid.NewGuid().ToString();
        swapMemoryProvider.AppUsed = 100 * VhUtils.Megabytes;
        await server.ConfigureAndSendStatus(TestCt);
        serverStatus = accessManager.LastServerStatus;
        Assert.AreEqual(2500 * VhUtils.Megabytes, swapMemoryProvider.Info.TotalSize);
        Assert.AreEqual(swapMemoryProvider.Info.TotalSize, accessManager.LastServerStatus?.TotalSwapMemory);
        Assert.AreEqual(swapMemoryProvider.Info.TotalSize - swapMemoryProvider.Info.TotalUsed,
            serverStatus?.AvailableSwapMemory);
    }

    [TestMethod]
    public async Task BadEndPoint_reported_for_invalid_endpoint()
    {
        // Arrange: occupy a port so the server can't bind to it
        using var accessManager = TestHelper.CreateAccessManager();
        using var blocker = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        var occupiedEndPoint = (IPEndPoint)blocker.LocalEndpoint;
        accessManager.ServerConfig.TcpEndPoints = [occupiedEndPoint];

        // autoStart: false because the server won't reach Ready state with a bad endpoint
        await using var server = await TestHelper.CreateServer(accessManager, autoStart: false);
        await server.Start(TestCt);

        // wait for bad config
        await VhTestUtil.AssertEqualsWait(ServerState.BadConfig, ()=>server.State, cancellationToken: TestCt);

        // the state flips to BadConfig before the status carrying EndPointStatuses reaches
        // the access manager, so wait for the report instead of reading it immediately
        await VhTestUtil.AssertEqualsWait(true, () => accessManager.LastEndPointStatuses != null,
            "EndPointStatuses should be reported", cancellationToken: TestCt);

        // Assert: the status reported to access manager contains EndPointStatuses with an error for the occupied endpoint
        var endPointStatuses = accessManager.LastEndPointStatuses;
        Assert.IsNotNull(endPointStatuses, "EndPointStatuses should be reported");
        Assert.IsNotEmpty(endPointStatuses, "EndPointStatuses should have at least one entry");

        var endPointError = endPointStatuses.FirstOrDefault(x =>
            x.Protocol == ChannelProtocol.Tcp && x.EndPoint.Equals(occupiedEndPoint));
        Assert.IsNotNull(endPointError, "EndPointStatuses should contain an entry for the occupied TCP endpoint");
        Assert.IsNotNull(endPointError.Error, "The occupied endpoint entry should have a non-null error");
        Assert.IsNotNull(accessManager.LastServerStatus?.ConfigError, "No TCP EndPoint error reported in ConfigError");
    }

    [TestMethod]
    public async Task Bad_Quic_endpoint_should_not_halt_configuration()
    {
        // Arrange: occupy a UDP port and point QUIC at it so the listener fails to bind.
        // A single bad endpoint must be recorded per-endpoint and must NOT fail the whole
        // server configuration. (Port 0 can't be used here: the access manager treats port 0
        // as "auto-assign" and replaces it with a free port.)
        using var accessManager = TestHelper.CreateAccessManager();
        using var blocker = new System.Net.Sockets.UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var occupiedEndPoint = (IPEndPoint)blocker.Client.LocalEndPoint!;
        accessManager.ServerConfig.QuicEndPoints = [occupiedEndPoint];

        await using var server = await TestHelper.CreateServer(accessManager);

        // Assert: the server still reaches Ready (TCP works); it must NOT halt on the bad QUIC endpoint.
        Assert.AreEqual(ServerState.Ready, server.State);

        // the state flips to Ready before the status carrying EndPointStatuses reaches
        // the access manager, so wait for the report instead of reading it immediately
        await VhTestUtil.AssertEqualsWait(true, () => accessManager.LastEndPointStatuses != null,
            "EndPointStatuses should be reported", cancellationToken: TestCt);

        // and configuration as a whole must not be reported as failed
        Assert.IsNull(accessManager.LastServerStatus?.ConfigError,
            "A bad QUIC endpoint must not fail the whole server configuration.");

        // Assert: the QUIC endpoint is reported as an errored endpoint in status (ignored, but surfaced).
        var endPointStatuses = accessManager.LastEndPointStatuses;
        Assert.IsNotNull(endPointStatuses, "EndPointStatuses should be reported");

        var quicStatus = endPointStatuses.FirstOrDefault(x => x.Protocol == ChannelProtocol.Quic);
        Assert.IsNotNull(quicStatus, "EndPointStatuses should contain an entry for the QUIC endpoint");
        Assert.IsNotNull(quicStatus.Error, "The bad QUIC endpoint should report a non-null error");

        // a working TCP endpoint must still be present and error-free
        Assert.Contains(x => x is { Protocol: ChannelProtocol.Tcp, Error: null }, endPointStatuses,
            "A working TCP endpoint should still be configured.");
    }

    // a closed session stays listed a while, so a UDP datagram its client still sends reaches it: its keys are gone, and
    // the datagram changes nothing
    [TestMethod]
    public async Task A_closed_session_takes_no_udp_datagram()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token);
        var session = server.GetSession(client);
        using var listener = new TestUdpListener();

        session.Dispose();
        Assert.IsFalse(session.ReceiveUdp(listener.Transmitter, UdpTestUtil.CreateForgedDatagram(session.SessionId),
            listener.LocalEndPoint));
        Assert.IsFalse(session.Tunnel.UseUdp);
    }

    // Compatibility (2026-10): a session below protocol 14 has the legacy UDP, which an old client's datagram turns on;
    // a client of protocol 14 takes no UDP from it
    [TestMethod]
    public async Task An_older_protocol_session_has_legacy_udp()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.SessionProtocolVersion = UdpDatagram.MinProtocolVersion - 1;
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token);
        var session = server.GetSession(client);
        using var listener = new TestUdpListener();

        Assert.IsNull(client.RequiredSession.Config.HostUdpEndPoint);
        var datagram = UdpTestUtil.SealLegacyDatagram(session.SessionId, session.SessionKey,
            UdpTestUtil.GetRandomSequenceNumber(), []);
        Assert.IsTrue(session.ReceiveUdp(listener.Transmitter, datagram, listener.LocalEndPoint));
        Assert.IsTrue(session.Tunnel.UseUdp);
    }

    // a UDP listener that cannot bind fails alone: the server starts, and its client connects over TCP and is offered
    // no UDP
    [TestMethod]
    public async Task Udp_listener_that_cannot_bind_leaves_the_server_on_tcp()
    {
        using var portHolder = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.UdpEndPoints = [portHolder.Client.GetLocalEndPoint()];
        using var accessManager = TestHelper.CreateAccessManager(fileAccessManagerOptions);
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token);

        Assert.IsNull(client.RequiredSession.Config.HostUdpEndPoint);
        await TestHelper.Test_Https();
    }

    // Compatibility (2026-10): a session below the legacy UDP's protocol has no UDP, as before protocol 14: its client
    // speaks an older one, so even a datagram sealed the legacy way makes nothing
    [TestMethod]
    public async Task A_session_below_the_legacy_protocol_has_no_udp()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.SessionProtocolVersion = UdpDatagram.MinLegacyProtocolVersion - 1;
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token);
        var session = server.GetSession(client);
        using var listener = new TestUdpListener();

        Assert.IsNull(session.UdpChannel);
        var datagram = UdpTestUtil.SealLegacyDatagram(session.SessionId, session.SessionKey,
            UdpTestUtil.GetRandomSequenceNumber(), []);
        Assert.IsFalse(session.ReceiveUdp(listener.Transmitter, datagram, listener.LocalEndPoint));
    }

    // a recovered session takes its protocol from the server's own record of it, not from the access manager, which
    // here kept the hello's
    [TestMethod]
    public async Task A_recovered_session_keeps_its_protocol()
    {
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        using var accessManager1 = TestHelper.CreateAccessManager(fileAccessManagerOptions);
        accessManager1.SessionProtocolVersion = UdpDatagram.MinProtocolVersion - 1;
        await using var server1 = await TestHelper.CreateServer(accessManager1);
        var token = TestHelper.CreateAccessToken(server1);
        await using var client = await TestHelper.CreateClient(token);
        Assert.AreEqual(UdpDatagram.MinProtocolVersion - 1, server1.GetSession(client).ProtocolVersion);

        await server1.DisposeAsync();
        accessManager1.Dispose();
        using var accessManager2 = TestHelper.CreateAccessManager(fileAccessManagerOptions, accessManager1.StoragePath);
        await using var server2 = await TestHelper.CreateServer(accessManager2);
        await TestHelper.Test_Https();

        Assert.AreEqual(UdpDatagram.MinProtocolVersion - 1, server2.GetSession(client).ProtocolVersion);
        await client.DisposeAsync(); // before server2
    }

    private async Task<HttpStatusCode> SendRawRequest(Token token, string request)
    {
        var serverEndPoint = token.ServerToken.HostEndPoints?.First()
                             ?? throw new InvalidOperationException("The token has no endpoint.");
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(serverEndPoint, TestCt);
        await using var sslStream = new SslStream(tcpClient.GetStream());
        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            TargetHost = token.ServerToken.HostName,
            RemoteCertificateValidationCallback = (_, _, _, _) => true
        }, TestCt);

        await sslStream.WriteAsync(Encoding.UTF8.GetBytes(request), TestCt);
        await sslStream.FlushAsync(TestCt);
        var response = await HttpUtils.ReadResponse(sslStream, TestCt);
        return response.StatusCode;
    }
}
