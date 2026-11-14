using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using VpnHood.Core.Client;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Test.Device;
using VpnHood.Test.Extensions;
using VpnHood.Test.Providers;
using ClientState = VpnHood.Core.Client.Abstractions.ClientState;

namespace VpnHood.Test.Tests;

[TestClass]
public class ServerNetFilterConfigTest : TestBase
{

    [TestMethod]
    public async Task VpnAdapter_Include()
    {
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.VpnAdapterIncludeIpRanges = [IpRange.Parse("130.0.0.100-130.0.0.250")];

        // Create Server
        await using var server = await TestHelper.CreateServer(serverOptions);
        var token = await TestHelper.CreateAccessToken(server);

        // create client
        var clientOptions = TestHelper.CreateClientOptions(token: token);
        clientOptions.IncludeIpRangesByDevice = [IpRange.Parse("130.0.0.0-130.0.0.200")];
        await using var client = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(),
            clientOptions: clientOptions);

        var includeNetworks = client.RequiredSession.Config.AdapterOptions.IncludeNetworks.ToIpRanges();
        Assert.IsNotNull(includeNetworks);
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("130.0.0.0")));
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("130.0.0.10")));
        Assert.IsTrue(includeNetworks.Contains(IPAddress.Parse("130.0.0.100")));
        Assert.IsTrue(includeNetworks.Contains(IPAddress.Parse("130.0.0.150")));
        Assert.IsTrue(includeNetworks.Contains(IPAddress.Parse("130.0.0.200")));
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("130.0.0.220")));
        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("130.0.0.50"));
    }

    [TestMethod]
    public async Task VpnAdapter_Exclude()
    {
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.VpnAdapterExcludeIpRanges = [IpRange.Parse("130.0.0.100-130.0.0.250")];

        // Create Server
        await using var server = await TestHelper.CreateServer(serverOptions);
        var token = await TestHelper.CreateAccessToken(server);

        // create client
        var clientOptions = TestHelper.CreateClientOptions(token: token);
        clientOptions.IncludeIpRangesByDevice = [IpRange.Parse("130.0.0.0-130.0.0.200")];
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions,
            vpnAdapter: new TestNullVpnAdapter());

        var includeNetworks = client.RequiredSession.Config.AdapterOptions.IncludeNetworks.ToIpRanges();
        Assert.IsNotNull(includeNetworks);
        Assert.IsTrue(includeNetworks.Contains(IPAddress.Parse("130.0.0.0")));
        Assert.IsTrue(includeNetworks.Contains(IPAddress.Parse("130.0.0.10")));
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("130.0.0.100")));
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("130.0.0.150")));
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("130.0.0.200")));

        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("130.0.0.220"))); //block by client
        Assert.IsFalse(server.SessionManager.NetFilter.IsIpBlocked("130.0.0.50"));
        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("130.0.0.220"));
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    public async Task VpnAdapter_Include_Exclude_LocalNetwork(bool serverAllowLocalNetworks, bool splitLocalNetwork)
    {
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.IncludeLocalNetwork = serverAllowLocalNetworks;
        serverOptions.NetFilterOptions.VpnAdapterIncludeIpRanges = [IpRange.Parse("000.0.0.000 - 230.0.0.220")];
        serverOptions.NetFilterOptions.VpnAdapterExcludeIpRanges = [IpRange.Parse("230.0.0.100 - 230.0.0.250")];

        // Create Server
        await using var server = await TestHelper.CreateServer(serverOptions);
        var token = await TestHelper.CreateAccessToken(server);

        // create client
        var clientOptions = TestHelper.CreateClientOptions(token: token);
        clientOptions.IncludeIpRangesByDevice = [.. IpNetwork.All.ToIpRanges()];
        clientOptions.SplitLocalNetwork = splitLocalNetwork;
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions,
            vpnAdapter: new TestNullVpnAdapter());

        // client: a split local network stays out of the VPN, whatever the server allows
        Assert.AreEqual(serverAllowLocalNetworks, client.RequiredSession.Info.IsLocalNetworkAllowed);
        Assert.AreEqual(serverAllowLocalNetworks && !splitLocalNetwork, client.RequiredSession.Config.AdapterOptions.IncludeNetworks.ToIpRanges().Contains(IPAddress.Parse("192.168.0.100")), "LocalNetWorks failed");

        var ipRanges = client.RequiredSession.Config.AdapterOptions.IncludeNetworks.ToIpRanges();
        Assert.IsNotNull(ipRanges);
        Assert.IsFalse(ipRanges.Contains(IPAddress.Parse("230.0.0.110")), "Excludes failed");
        Assert.AreEqual(!splitLocalNetwork, ipRanges.Contains(IPAddress.Parse("230.0.0.50")), "Includes failed"); // multicast goes with the split
        Assert.IsFalse(ipRanges.Contains(IPAddress.Parse("230.0.0.240")), "Includes failed");
        Assert.IsFalse(ipRanges.Contains(IPAddress.Parse("230.0.0.254")), "Includes failed");

        // server: multicast is blocked with the local networks
        Assert.AreNotEqual(serverAllowLocalNetworks,server.SessionManager.NetFilter.IsIpBlocked("192.168.0.100"));
        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("230.0.0.110"));
        Assert.AreNotEqual(serverAllowLocalNetworks, server.SessionManager.NetFilter.IsIpBlocked("230.0.0.50"));
        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("230.0.0.254"));
    }

    [TestMethod]
    public async Task IpRange_Include_Exclude()
    {
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.IncludeIpRanges = [IpRange.Parse("000.0.0.000 - 130.0.0.220")];
        serverOptions.NetFilterOptions.ExcludeIpRanges = [IpRange.Parse("130.0.0.100 - 130.0.0.250")];

        // Create Server
        await using var server = await TestHelper.CreateServer(serverOptions);
        var token = await TestHelper.CreateAccessToken(server);

        // create client
        var clientOptions = TestHelper.CreateClientOptions(token: token);
        clientOptions.IncludeIpRangesByDevice = [.. IpNetwork.All.ToIpRanges()];
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions,
            vpnAdapter: new TestNullVpnAdapter());

        Assert.IsFalse(client.ServerIpFilter.IncludeRanges.Contains(IPAddress.Parse("130.0.0.110")), "Excludes failed");
        Assert.IsTrue(client.ServerIpFilter.IncludeRanges.Contains(IPAddress.Parse("130.0.0.50")), "Includes failed");
        Assert.IsFalse(client.ServerIpFilter.IncludeRanges.Contains(IPAddress.Parse("130.0.0.240")), "Includes failed");
        Assert.IsFalse(client.ServerIpFilter.IncludeRanges.Contains(IPAddress.Parse("130.0.0.254")), "Includes & Excludes failed");

        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("130.0.0.110"));
        Assert.IsFalse(server.SessionManager.NetFilter.IsIpBlocked("130.0.0.50"));
        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("130.0.0.254"));
    }

    // TCP forwarding connects from the server's own process, to which its loopback is as near as to any program of
    // the machine. With the local network included the same request gets through, which shows it is the block
    // that stops it.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_session_reaches_a_service_on_the_servers_loopback_only_with_the_local_network(
        bool includeLocalNetwork)
    {
        // not 127.0.0.1: these servers listen there, and so block it as their own address
        using var loopbackService = new TcpListener(IPAddress.Parse("127.33.0.1"), 0);
        loopbackService.Start();

        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.IncludeLocalNetwork = includeLocalNetwork;
        await using var server = await TestHelper.CreateServer(serverOptions);
        var token = await TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token, vpnAdapter: new TestNullVpnAdapter());
        var session = (ClientSession)client.RequiredSession;

        var request = new TcpForwardingChannelRequest {
            RequestId = UniqueIdFactory.Create(),
            SessionId = session.Config.SessionId,
            SessionKey = session.Config.SessionKey,
            DestinationEndPoint = (IPEndPoint)loopbackService.LocalEndpoint
        };

        if (includeLocalNetwork) {
            await using var result = await session.SendRequest<SessionResponse>(request, TestCt);
            using var accepted = await loopbackService.AcceptTcpClientAsync(TestCt);
            return;
        }

        var ex = await Assert.ThrowsAsync<Exception>(() => session.SendRequest<SessionResponse>(request, TestCt));
        Assert.IsNotInstanceOfType<OperationCanceledException>(ex, "The request was canceled, not refused.");
        Assert.IsFalse(loopbackService.Pending(), "The server connected to its own loopback for a client.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task What_is_not_public_is_blocked_unless_the_local_network_is_included(bool includeLocalNetwork)
    {
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.IncludeLocalNetwork = includeLocalNetwork;
        await using var server = await TestHelper.CreateServer(serverOptions);
        var netFilter = server.SessionManager.NetFilter;

        // the loopback beyond these servers' own 127.0.0.1, a resolver's stub, "this network", the carriers' shared
        // space with a cloud's metadata in it, multicast, the reserved space, and the same in IPv6's forms
        string[] nonPublic = [
            "127.0.0.2", "127.0.0.53", "0.0.0.0", "100.64.0.1", "100.100.100.200", "192.0.0.8", "224.0.0.251",
            "240.0.0.1", "255.255.255.255", "10.0.0.1", "169.254.169.254", "::ffff:127.0.0.2", "::", "::1", "fd00::1",
            "fe80::1", "ff02::1", "64:ff9b::7f00:1", "2002:7f00:1::1"
        ];
        foreach (var address in nonPublic)
            Assert.AreEqual(!includeLocalNetwork, netFilter.IsIpBlocked(address), address);

        string[] publicAddresses = ["8.8.8.8", "::ffff:8.8.8.8", "2001:4860:4860::8888"];
        foreach (var address in publicAddresses)
            Assert.IsFalse(netFilter.IsIpBlocked(address), address);
    }

    // The tests' servers reach their mock hosts at addresses that are not public. A server made as an app makes
    // it, with no list of its own, blocks those too.
    [TestMethod]
    public async Task A_server_blocks_the_tests_own_networks_unless_it_is_made_for_the_tests()
    {
        using var accessManager = await TestHelper.CreateAccessManager();
        await using var server = new VpnHoodServer(accessManager, new ServerOptions {
            SocketFactory = new TestSocketFactory(),
            StoragePath = TestHelper.WorkingPath,
            AutoDisposeAccessManager = false,
            PublicIpDiscovery = false
        });
        await server.Start(TestCt);
        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("198.18.0.1"));
        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("2001:db8::1"));
        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("127.0.0.2"));

        await using var testServer = await TestHelper.CreateServer();
        Assert.IsFalse(testServer.SessionManager.NetFilter.IsIpBlocked("198.18.0.1"));
        Assert.IsFalse(testServer.SessionManager.NetFilter.IsIpBlocked("2001:db8::1"));
        Assert.IsTrue(testServer.SessionManager.NetFilter.IsIpBlocked("127.0.0.2"));

        // and the tests' list opens nothing else
        var opened = IpNetwork.NonPublicNetworks.ToIpRanges().Exclude(TestHelper.ServerNonPublicNetworks.ToIpRanges());
        CollectionAssert.AreEqual(
            new[] { IpRange.Parse("198.18.0.0/15"), IpRange.Parse("2001:db8::/32") }, opened.ToArray());
    }

    [TestMethod]
    public async Task BlockPorts_default_blocks_tcp_25_only()
    {
        await using var server = await TestHelper.CreateServer();
        var netFilter = server.SessionManager.NetFilter;

        Assert.IsTrue(netFilter.IsPortBlocked(IpProtocol.Tcp, 25));
        Assert.IsFalse(netFilter.IsPortBlocked(IpProtocol.Tcp, 587));
        Assert.IsFalse(netFilter.IsPortBlocked(IpProtocol.Tcp, 465));
        Assert.IsFalse(netFilter.IsPortBlocked(IpProtocol.Udp, 25));
    }

    [TestMethod]
    public async Task BlockPorts_set_replace_the_default()
    {
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.BlockTcpPorts = [];
        serverOptions.NetFilterOptions.BlockUdpPorts = [PortRange.Parse("6881-6889"), new PortRange(1900)];
        await using var server = await TestHelper.CreateServer(serverOptions);
        var netFilter = server.SessionManager.NetFilter;

        Assert.IsFalse(netFilter.IsPortBlocked(IpProtocol.Tcp, 25));
        Assert.IsTrue(netFilter.IsPortBlocked(IpProtocol.Udp, 6881));
        Assert.IsTrue(netFilter.IsPortBlocked(IpProtocol.Udp, 6889));
        Assert.IsTrue(netFilter.IsPortBlocked(IpProtocol.Udp, 1900));
        Assert.IsFalse(netFilter.IsPortBlocked(IpProtocol.Udp, 6880));
        Assert.IsFalse(netFilter.IsPortBlocked(IpProtocol.Udp, 6890));
        Assert.IsFalse(netFilter.IsPortBlocked(IpProtocol.Tcp, 6885));
    }

    [TestMethod]
    public void BlockPorts_json()
    {
        var options = JsonSerializer.Deserialize<NetFilterOptions>("""{"BlockTcpPorts":["25"," 6881 - 6889 "]}""");
        Assert.IsNotNull(options?.BlockTcpPorts);
        CollectionAssert.AreEqual(new[] { new PortRange(25), new PortRange(6881, 6889) },
            options.BlockTcpPorts.ToArray());
        Assert.IsNull(options.BlockUdpPorts);
        StringAssert.Contains(JsonSerializer.Serialize(options), "\"BlockTcpPorts\":[\"25\",\"6881-6889\"]");

        Assert.ThrowsExactly<FormatException>(() => PortRange.Parse("smtp"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PortRange.Parse("70000"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PortRange.Parse("30-20"));
    }

    [TestMethod]
    public async Task BlockPorts_drop_without_closing_the_session()
    {
        var blockedUdpEndPoint = MockEps.UdpV4EndPoint1;
        var openUdpEndPoint = MockEps.UdpV4EndPoint2;
        var blockedTcpEndPoint = MockEps.TcpUploadEndPoint1;
        var openTcpEndPoint = MockEps.TcpDownloadEndPoint1;
        if (blockedUdpEndPoint.Port == openUdpEndPoint.Port || blockedTcpEndPoint.Port == openTcpEndPoint.Port)
            Assert.Inconclusive("The test servers took the same port.");

        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.BlockTcpPorts = [new PortRange(blockedTcpEndPoint.Port)];
        serverOptions.NetFilterOptions.BlockUdpPorts = [new PortRange(blockedUdpEndPoint.Port)];
        await using var server = await TestHelper.CreateServer(serverOptions);
        var token = await TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token);

        // the blocked ports get no answer, again and again
        for (var i = 0; i < 3; i++) {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                TestHelper.Test_UdpEcho(blockedUdpEndPoint, timeout: TimeSpan.FromSeconds(1)));
            await Assert.ThrowsAsync<Exception>(() =>
                TestHelper.Test_TcpUpload(tcpEndPoint: blockedTcpEndPoint, timeout: TimeSpan.FromSeconds(3)));
        }

        // the session goes on for every other port
        await TestHelper.Test_UdpEcho(openUdpEndPoint);
        await TestHelper.Test_TcpDownload(tcpEndPoint: openTcpEndPoint);
        await TestHelper.Test_Https();
        Assert.AreEqual(ClientState.Connected, client.State);
    }
}