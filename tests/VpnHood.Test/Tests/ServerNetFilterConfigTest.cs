using System.Net;
using System.Text.Json;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Test.Device;
using VpnHood.Test.Extensions;
using ClientState = VpnHood.Core.Client.Abstractions.ClientState;

namespace VpnHood.Test.Tests;

[TestClass]
public class ServerNetFilterConfigTest : TestBase
{

    [TestMethod]
    public async Task VpnAdapter_Include()
    {
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.VpnAdapterIncludeIpRanges = [IpRange.Parse("230.0.0.100-230.0.0.250")];

        // Create Server
        await using var server = await TestHelper.CreateServer(serverOptions);
        var token = await TestHelper.CreateAccessToken(server);

        // create client
        var clientOptions = TestHelper.CreateClientOptions(token: token);
        clientOptions.IncludeIpRangesByDevice = [IpRange.Parse("230.0.0.0-230.0.0.200")];
        await using var client = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(),
            clientOptions: clientOptions);

        var includeNetworks = client.RequiredSession.Config.AdapterOptions.IncludeNetworks.ToIpRanges();
        Assert.IsNotNull(includeNetworks);
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("230.0.0.0")));
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("230.0.0.10")));
        Assert.IsTrue(includeNetworks.Contains(IPAddress.Parse("230.0.0.100")));
        Assert.IsTrue(includeNetworks.Contains(IPAddress.Parse("230.0.0.150")));
        Assert.IsTrue(includeNetworks.Contains(IPAddress.Parse("230.0.0.200")));
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("230.0.0.220")));
        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("230.0.0.50"));
    }

    [TestMethod]
    public async Task VpnAdapter_Exclude()
    {
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.VpnAdapterExcludeIpRanges = [IpRange.Parse("230.0.0.100-230.0.0.250")];

        // Create Server
        await using var server = await TestHelper.CreateServer(serverOptions);
        var token = await TestHelper.CreateAccessToken(server);

        // create client
        var clientOptions = TestHelper.CreateClientOptions(token: token);
        clientOptions.IncludeIpRangesByDevice = [IpRange.Parse("230.0.0.0-230.0.0.200")];
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions,
            vpnAdapter: new TestNullVpnAdapter());

        var includeNetworks = client.RequiredSession.Config.AdapterOptions.IncludeNetworks.ToIpRanges();
        Assert.IsNotNull(includeNetworks);
        Assert.IsTrue(includeNetworks.Contains(IPAddress.Parse("230.0.0.0")));
        Assert.IsTrue(includeNetworks.Contains(IPAddress.Parse("230.0.0.10")));
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("230.0.0.100")));
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("230.0.0.150")));
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("230.0.0.200")));

        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("230.0.0.220"))); //block by client
        Assert.IsFalse(server.SessionManager.NetFilter.IsIpBlocked("230.0.0.50"));
        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("230.0.0.220"));
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

        // server
        Assert.AreNotEqual(serverAllowLocalNetworks,server.SessionManager.NetFilter.IsIpBlocked("192.168.0.100"));
        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("230.0.0.110"));
        Assert.IsFalse(server.SessionManager.NetFilter.IsIpBlocked("230.0.0.50"));
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