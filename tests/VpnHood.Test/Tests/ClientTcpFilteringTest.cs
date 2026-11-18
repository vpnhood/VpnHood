using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Client;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Test.Extensions;
using ClientState = VpnHood.Core.Client.Abstractions.ClientState;
using SplitDnsMode = VpnHood.Core.Client.Abstractions.SplitDnsMode;

namespace VpnHood.Test.Tests;

// How the client decides a TCP connection. Its TCP host decides the connection once more by its stream
// (ClientStreamHandler), whatever the packet path chose for its packets, so the domain rules, the IP rules, an ad's
// passthrough and DNS kept inside the VPN must come out on that path as on the packet path.
[TestClass]
public class ClientTcpFilteringTest : TestBase
{
    private NetFilter CreateNetFilter(IIpFilter? ipFilter, IDomainFilter? domainFilter = null)
    {
        return new NetFilter {
            IpFilter = ipFilter,
            DomainFilter = domainFilter,
            IpMapper = TestHelper.ClientNetFilter.IpMapper
        };
    }

    // The test's mapper, which blocks an address in the IP gate as it first maps it, as a user's edit can come while a
    // connection decided around the VPN is being set up: the mapping comes after the decision, before the connection
    private sealed class ChangingIpMapper(IIpMapper ipMapper, StaticIpFilter ipFilter, IPAddress address) : IIpMapper
    {
        private int _isChanged;

        public bool ToHost(IpProtocol protocol, IpEndPointValue hostEndPoint, out IpEndPointValue newEndPoint)
        {
            if (hostEndPoint.ToIPEndPoint().Address.Equals(address) && Interlocked.Exchange(ref _isChanged, 1) == 0)
                ipFilter.BlockedRanges = new[] { address }.ToOrderedIpRanges();

            return ipMapper.ToHost(protocol, hostEndPoint, out newEndPoint);
        }

        public bool FromHost(IpProtocol protocol, IpEndPointValue hostEndPoint, out IpEndPointValue newEndPoint) =>
            ipMapper.FromHost(protocol, hostEndPoint, out newEndPoint);

        public void Dispose()
        {
        }
    }

    private async Task<TcpClient> AcceptWithin(TcpListener listener, TimeSpan timeout)
    {
        using var acceptCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        acceptCts.CancelAfter(timeout);
        return await listener.AcceptTcpClientAsync(acceptCts.Token);
    }

    // a listener on a DNS port, 53 or DoT's 853, which a connection reaches both ways: around the VPN through the
    // client's mapper, and through the VPN by the server's
    private (TcpListener Listener, IPEndPoint RemoteEndPoint) StartTcpDnsListener(int port = 53)
    {
        var listener = new TcpListener(TestHelper.TestIps.LocalNsTestIp, port);
        listener.Start();
        return (listener, new IPEndPoint(TestHelper.TestIps.RemoteNsTestIp, port));
    }

    [TestMethod]
    public async Task A_server_that_speaks_first_is_heard_with_a_domain_rule_on()
    {
        await using var server = await TestHelper.CreateServer();
        var netFilter = CreateNetFilter(TestHelper.ClientNetFilter.IpFilter,
            new StaticDomainFilter(null) { Blocks = ["a-name-nobody-asks-for.example"] });
        await using var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server)), netFilter: netFilter);
        await client.WaitForState(ClientState.Connected, TestCt);

        // The test server sends its data as soon as it accepts, and the app sends nothing first, as a mail or FTP
        // client: the client waited for the app's first bytes before it opened the connection to the server, and
        // neither side ever spoke
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(MockEps.TcpDataEndPoint1, TestCt);
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        readCts.CancelAfter(TimeSpan.FromSeconds(10));
        await tcpClient.GetStream().ReadExactlyAsync(new byte[100], readCts.Token);
    }

    [TestMethod]
    public async Task A_domain_include_passes_an_IP_block_on_TCP_as_on_QUIC()
    {
        // the HTTPS test server's address is blocked, and its name included
        var serverAddress = TestHelper.TestIps.MapToRemote(IPAddress.Parse(TestHelper.WebServer.LocalEps.HttpsUrl1.Host));
        await using var server = await TestHelper.CreateServer();
        var netFilter = CreateNetFilter(
            new StaticIpFilter(null) { BlockedRanges = new[] { serverAddress }.ToOrderedIpRanges() },
            new StaticDomainFilter(null) { Includes = [MockEps.HttpsUrl1.Host] });
        await using var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server)), netFilter: netFilter);
        await client.WaitForState(ClientState.Connected, TestCt);

        // A QUIC flow's name is asked before its address, and a domain include goes past every IP gate; a TCP
        // connection's packets met the IP block first, and were dropped before its name was read
        await TestHelper.Test_Https(timeout: TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public async Task A_rule_changed_while_connected_ends_the_TCP_connections()
    {
        // a server that keeps its connection open
        var localEndPoint = TestWebServerLocalEps.AllocateFreeTcpEndPoint(TestHelper.TestIps.LocalNsTestIp);
        using var listener = new TcpListener(localEndPoint);
        listener.Start();
        var remoteEndPoint = new IPEndPoint(TestHelper.TestIps.RemoteNsTestIp, localEndPoint.Port);

        await using var server = await TestHelper.CreateServer();
        var ipFilter = new StaticIpFilter(null);
        var netFilter = CreateNetFilter(ipFilter,
            new StaticDomainFilter(null) { Blocks = ["a-name-nobody-asks-for.example"] });
        await using var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server)), netFilter: netFilter);
        await client.WaitForState(ClientState.Connected, TestCt);

        // the app speaks first, so the connection is open at once, its name read as none
        using var setupCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        setupCts.CancelAfter(TimeSpan.FromSeconds(10));
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(remoteEndPoint, setupCts.Token);
        await tcpClient.GetStream().WriteAsync(new byte[] { 1 }, setupCts.Token);
        using var accepted = await listener.AcceptTcpClientAsync(setupCts.Token);
        await accepted.GetStream().ReadExactlyAsync(new byte[1], setupCts.Token);

        // The user blocks its address while it is open. With a domain rule on, a TCP connection is decided once, by
        // its name first, and its packets meet no IP gate: it went on
        ipFilter.BlockedRanges = new[] { remoteEndPoint.Address }.ToOrderedIpRanges();

        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        readCts.CancelAfter(TimeSpan.FromSeconds(10));
        try {
            Assert.AreEqual(0, await tcpClient.GetStream().ReadAsync(new byte[1], readCts.Token));
        }
        catch (IOException) {
            // ended by a reset
        }
    }

    [TestMethod]
    public async Task A_connection_decided_as_the_rules_change_does_not_go_out()
    {
        var localEndPoint = TestWebServerLocalEps.AllocateFreeTcpEndPoint(TestHelper.TestIps.LocalNsTestIp);
        using var listener = new TcpListener(localEndPoint);
        listener.Start();
        var remoteEndPoint = new IPEndPoint(TestHelper.TestIps.RemoteNsTestIp, localEndPoint.Port);

        await using var server = await TestHelper.CreateServer();
        var ipFilter = new StaticIpFilter(null) {
            ExcludeRanges = new[] { remoteEndPoint.Address }.ToOrderedIpRanges()
        };
        var netFilter = new NetFilter {
            IpFilter = ipFilter,
            DomainFilter = new StaticDomainFilter(null) { Blocks = ["a-name-nobody-asks-for.example"] },
            IpMapper = new ChangingIpMapper(TestHelper.ClientNetFilter.IpMapper!, ipFilter, remoteEndPoint.Address)
        };
        await using var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server)), netFilter: netFilter);
        await client.WaitForState(ClientState.Connected, TestCt);

        // The app speaks first, so its connection is decided at once, by the old rules, around the VPN, and the change
        // that blocks its address comes as it is set up. The change ended the connections open, and this one went out
        using var setupCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        setupCts.CancelAfter(TimeSpan.FromSeconds(10));
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(remoteEndPoint, setupCts.Token);
        await tcpClient.GetStream().WriteAsync(new byte[] { 1 }, setupCts.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(() => AcceptWithin(listener, TimeSpan.FromSeconds(3)));
    }

    [TestMethod]
    [DataRow(53, SplitDnsMode.IncludeAll, true)]
    [DataRow(853, SplitDnsMode.IncludeAll, true)]
    [DataRow(53, SplitDnsMode.DefaultRoute, false)]
    public async Task TCP_DNS_stays_in_the_VPN_under_IncludeAll_where_a_split_excludes_its_server(int port,
        SplitDnsMode splitDnsMode, bool isTunnelled)
    {
        var (dnsListener, dnsEndPoint) = StartTcpDnsListener(port);
        using var dnsListenerScope = dnsListener;
        await using var server = await TestHelper.CreateServer();
        var netFilter = CreateNetFilter(
            new StaticIpFilter(null) { ExcludeRanges = new[] { dnsEndPoint.Address }.ToOrderedIpRanges() });
        var clientOptions = TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server));
        clientOptions.SplitDnsMode = splitDnsMode;
        await using var client = await TestHelper.CreateClient(clientOptions, netFilter: netFilter);
        await client.WaitForState(ClientState.Connected, TestCt);

        // The packet path kept the query inside, and the TCP forwarding then decided the connection again by its
        // address alone, and took it around the VPN. Under DefaultRoute, DNS follows the split, around
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(dnsEndPoint, TestCt);
        using var accepted = await AcceptWithin(dnsListener, TimeSpan.FromSeconds(10));
        await AssertEqualsWait(isTunnelled ? 1 : 0, () => client.RequiredSession.Status.StreamTunnelledCount);
        await AssertEqualsWait(isTunnelled ? 0 : 1, () => client.RequiredSession.Status.StreamPassthruCount);
    }

    [TestMethod]
    public async Task TCP_DNS_stays_in_the_VPN_while_an_ad_passes_other_traffic_around_it()
    {
        var (dnsListener, dnsEndPoint) = StartTcpDnsListener();
        using var dnsListenerScope = dnsListener;
        await using var server = await TestHelper.CreateServer();
        var clientOptions = TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server));
        clientOptions.SplitDnsMode = SplitDnsMode.IncludeAll;
        await using var client = await TestHelper.CreateClient(clientOptions);
        await client.WaitForState(ClientState.Connected, TestCt);

        // While an ad shows, its traffic goes around the VPN, so the ad's network sees no VPN, and DNS stays inside;
        // the TCP forwarding took a DNS connection around too
        ((ClientSession)client.RequiredSession).PassthroughState.PassthroughForAd = true;
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(dnsEndPoint, TestCt);
        using var accepted = await AcceptWithin(dnsListener, TimeSpan.FromSeconds(10));
        await AssertEqualsWait(1, () => client.RequiredSession.Status.StreamTunnelledCount);
        Assert.AreEqual(0, client.RequiredSession.Status.StreamPassthruCount);

        // and the rest goes around
        await TestHelper.Test_Https();
        Assert.IsGreaterThan(0, client.RequiredSession.Status.StreamPassthruCount);
    }
}
