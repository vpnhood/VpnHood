using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client;
using VpnHood.Core.Common.Configuration;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Forwarding;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Test.Extensions;
using VpnHood.Test.Providers;
using ClientState = VpnHood.Core.Client.Abstractions.ClientState;
using SplitDnsMode = VpnHood.Core.Client.Abstractions.SplitDnsMode;
using SplitUnsupportedIpMode = VpnHood.Core.Client.Abstractions.SplitUnsupportedIpMode;

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

    // The test's socket factory, which blocks an address in the IP gate as the client makes the socket to reach its
    // host: after the connection was decided, and checked against a change of the rules, before it is connected
    private sealed class RuleChangingSocketFactory(StaticIpFilter ipFilter, IPEndPoint hostEndPoint, IPAddress address)
        : TestSocketFactory
    {
        private int _isChanged;

        public override TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
        {
            if (ipEndPoint.Equals(hostEndPoint) && Interlocked.Exchange(ref _isChanged, 1) == 0)
                ipFilter.BlockedRanges = new[] { address }.ToOrderedIpRanges();

            return base.CreateTcpClient(ipEndPoint, options);
        }
    }

    // a TCP host that only keeps the packets it is given
    private sealed class RecordingTcpHost : IClientTcpHost
    {
        public List<IpPacket> Packets { get; } = [];

        public event EventHandler<IpPacket>? PacketReceived {
            add { }
            remove { }
        }

        public void ProcessOutgoingPacket(IpPacket ipPacket) => Packets.Add(ipPacket);

        public void DropCurrentConnections()
        {
        }

        public void Start()
        {
        }

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

        // through the VPN, as an include promises: both ways reach the test server
        Assert.IsGreaterThan(0, client.RequiredSession.Status.StreamTunnelledCount);
        Assert.AreEqual(0, client.RequiredSession.Status.StreamPassthruCount);
    }

    [TestMethod]
    public async Task An_IP_block_stops_a_TCP_connection_that_no_domain_entry_names()
    {
        // the HTTPS test server's address is blocked, and a domain rule is on that does not name it
        var serverAddress = TestHelper.TestIps.MapToRemote(IPAddress.Parse(TestHelper.WebServer.LocalEps.HttpsUrl1.Host));
        await using var server = await TestHelper.CreateServer();
        var netFilter = CreateNetFilter(
            new StaticIpFilter(null) { BlockedRanges = new[] { serverAddress }.ToOrderedIpRanges() },
            new StaticDomainFilter(null) { Blocks = ["a-name-nobody-asks-for.example"] });
        await using var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server)), netFilter: netFilter);
        await client.WaitForState(ClientState.Connected, TestCt);

        // with a domain rule on, the block is met once the connection's name is read, no longer at its packets
        Assert.IsFalse(await TestHelper.Test_Https(timeout: TimeSpan.FromSeconds(5), throwError: false));
        Assert.AreEqual(0, client.RequiredSession.Status.StreamTunnelledCount);
        Assert.AreEqual(0, client.RequiredSession.Status.StreamPassthruCount);
    }

    [TestMethod]
    public async Task A_domain_exclude_passes_an_IP_block_on_TCP_as_on_QUIC()
    {
        // the HTTPS test server's address is blocked, and its name excluded
        var serverAddress = TestHelper.TestIps.MapToRemote(IPAddress.Parse(TestHelper.WebServer.LocalEps.HttpsUrl1.Host));
        await using var server = await TestHelper.CreateServer();
        var netFilter = CreateNetFilter(
            new StaticIpFilter(null) { BlockedRanges = new[] { serverAddress }.ToOrderedIpRanges() },
            new StaticDomainFilter(null) { Excludes = [MockEps.HttpsUrl1.Host] });
        await using var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server)), netFilter: netFilter);
        await client.WaitForState(ClientState.Connected, TestCt);

        // a name's own verdict decides its connection, an exclude as an include: around the VPN, past the IP block
        await TestHelper.Test_Https(timeout: TimeSpan.FromSeconds(10));
        Assert.IsGreaterThan(0, client.RequiredSession.Status.StreamPassthruCount);
        Assert.AreEqual(0, client.RequiredSession.Status.StreamTunnelledCount);
    }

    [TestMethod]
    public async Task A_blocked_name_stays_blocked_while_an_ad_shows()
    {
        await using var server = await TestHelper.CreateServer();
        var netFilter = CreateNetFilter(TestHelper.ClientNetFilter.IpFilter,
            new StaticDomainFilter(null) { Blocks = [MockEps.HttpsUrl1.Host] });
        await using var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server)), netFilter: netFilter);
        await client.WaitForState(ClientState.Connected, TestCt);

        // While an ad shows, its traffic goes around the VPN. The connection's block was taken for an exclude with
        // the rest, and the blocked name went around too
        ((ClientSession)client.RequiredSession).PassthroughState.PassthroughForAd = true;
        Assert.IsFalse(await TestHelper.Test_Https(timeout: TimeSpan.FromSeconds(5), throwError: false));
        Assert.AreEqual(0, client.RequiredSession.Status.StreamPassthruCount);

        // and a name with no rule goes around, as the ad's does
        Assert.IsTrue(await TestHelper.Test_Https(MockEps.HttpsUrl2, throwError: false));
        Assert.IsGreaterThan(0, client.RequiredSession.Status.StreamPassthruCount);
    }

    [TestMethod]
    public async Task A_ClientHello_larger_than_4_KB_reaches_its_server_with_a_domain_rule_on()
    {
        var localEndPoint = TestWebServerLocalEps.AllocateFreeTcpEndPoint(TestHelper.TestIps.LocalNsTestIp);
        using var listener = new TcpListener(localEndPoint);
        listener.Start();
        var remoteEndPoint = new IPEndPoint(TestHelper.TestIps.RemoteNsTestIp, localEndPoint.Port);

        await using var server = await TestHelper.CreateServer();
        var netFilter = CreateNetFilter(TestHelper.ClientNetFilter.IpFilter,
            new StaticDomainFilter(null) { Blocks = ["a-name-nobody-asks-for.example"] });
        await using var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server)), netFilter: netFilter);
        await client.WaitForState(ClientState.Connected, TestCt);

        // A ClientHello of 6 KB, as one with large extensions: the client read 4 KB for the name, which the reader
        // now takes for a ClientHello cut short, and the connection would fail; its read holds a whole TLS record
        var clientHello = DomainFilteringServiceTest.BuildClientHello("www.allowed.example", paddingLength: 6000);
        using var ioCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        ioCts.CancelAfter(TimeSpan.FromSeconds(10));
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(remoteEndPoint, ioCts.Token);
        await tcpClient.GetStream().WriteAsync(clientHello, ioCts.Token);
        using var accepted = await listener.AcceptTcpClientAsync(ioCts.Token);
        var received = new byte[clientHello.Length];
        await accepted.GetStream().ReadExactlyAsync(received, ioCts.Token);
        CollectionAssert.AreEqual(clientHello, received);
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
    public async Task A_reconfigure_that_changes_no_rule_keeps_the_TCP_connections()
    {
        // a server that keeps its connection open
        var localEndPoint = TestWebServerLocalEps.AllocateFreeTcpEndPoint(TestHelper.TestIps.LocalNsTestIp);
        using var listener = new TcpListener(localEndPoint);
        listener.Start();
        var remoteEndPoint = new IPEndPoint(TestHelper.TestIps.RemoteNsTestIp, localEndPoint.Port);

        await using var server = await TestHelper.CreateServer();
        var netFilter = CreateNetFilter(new StaticIpFilter(null),
            new StaticDomainFilter(null) { Blocks = ["a-name-nobody-asks-for.example"] });
        await using var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server)), netFilter: netFilter);
        await client.WaitForState(ClientState.Connected, TestCt);

        using var ioCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        ioCts.CancelAfter(TimeSpan.FromSeconds(10));
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(remoteEndPoint, ioCts.Token);
        await tcpClient.GetStream().WriteAsync(new byte[] { 1 }, ioCts.Token);
        using var accepted = await listener.AcceptTcpClientAsync(ioCts.Token);
        await accepted.GetStream().ReadExactlyAsync(new byte[1], ioCts.Token);

        // The app sends every live setting with each reconfigure, as on any setting saved while connected, these
        // two among them. Set to what they are, they change no rule: the connections were ended all the same
        client.UnroutedIpMode = client.UnroutedIpMode;
        client.UnsupportedIpV6Mode = client.UnsupportedIpV6Mode;

        // the connection still carries data both ways
        await tcpClient.GetStream().WriteAsync(new byte[] { 2 }, ioCts.Token);
        await accepted.GetStream().ReadExactlyAsync(new byte[1], ioCts.Token);
        await accepted.GetStream().WriteAsync(new byte[] { 3 }, ioCts.Token);
        await tcpClient.GetStream().ReadExactlyAsync(new byte[1], ioCts.Token);
    }

    [TestMethod]
    public async Task A_rule_changed_with_no_domain_rule_on_ends_no_connection()
    {
        // a server that keeps its connection open
        var localEndPoint = TestWebServerLocalEps.AllocateFreeTcpEndPoint(TestHelper.TestIps.LocalNsTestIp);
        using var listener = new TcpListener(localEndPoint);
        listener.Start();
        var remoteEndPoint = new IPEndPoint(TestHelper.TestIps.RemoteNsTestIp, localEndPoint.Port);

        await using var server = await TestHelper.CreateServer();
        var ipFilter = new StaticIpFilter(null);
        await using var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server), useTcpForwarding: true),
            netFilter: CreateNetFilter(ipFilter));
        await client.WaitForState(ClientState.Connected, TestCt);

        using var ioCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        ioCts.CancelAfter(TimeSpan.FromSeconds(10));
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(remoteEndPoint, ioCts.Token);
        await tcpClient.GetStream().WriteAsync(new byte[] { 1 }, ioCts.Token);
        using var accepted = await listener.AcceptTcpClientAsync(ioCts.Token);
        await accepted.GetStream().ReadExactlyAsync(new byte[1], ioCts.Token);

        // The user blocks another address. With no domain rule on, each packet of a TCP connection still meets the IP
        // gates, where a block of its own address would stop it, so nothing needs ending: every connection was ended
        ipFilter.BlockedRanges = new[] { IPAddress.Parse("192.0.2.1") }.ToOrderedIpRanges();

        await tcpClient.GetStream().WriteAsync(new byte[] { 2 }, ioCts.Token);
        await accepted.GetStream().ReadExactlyAsync(new byte[1], ioCts.Token);
        await accepted.GetStream().WriteAsync(new byte[] { 3 }, ioCts.Token);
        await tcpClient.GetStream().ReadExactlyAsync(new byte[1], ioCts.Token);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task The_last_domain_rule_that_goes_and_the_first_that_comes_end_the_TCP_connections(bool isLastRule)
    {
        // a server that keeps its connection open
        var localEndPoint = TestWebServerLocalEps.AllocateFreeTcpEndPoint(TestHelper.TestIps.LocalNsTestIp);
        using var listener = new TcpListener(localEndPoint);
        listener.Start();
        var remoteEndPoint = new IPEndPoint(TestHelper.TestIps.RemoteNsTestIp, localEndPoint.Port);

        // TCP forwarding is on by the user's setting, so the connections stay with the TCP host either way
        string[] rules = ["a-name-nobody-asks-for.example"];
        await using var server = await TestHelper.CreateServer();
        var domainFilter = new StaticDomainFilter(null) { Blocks = isLastRule ? rules : [] };
        await using var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server), useTcpForwarding: true),
            netFilter: CreateNetFilter(new StaticIpFilter(null), domainFilter));
        await client.WaitForState(ClientState.Connected, TestCt);

        using var setupCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        setupCts.CancelAfter(TimeSpan.FromSeconds(10));
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(remoteEndPoint, setupCts.Token);
        await tcpClient.GetStream().WriteAsync(new byte[] { 1 }, setupCts.Token);
        using var accepted = await listener.AcceptTcpClientAsync(setupCts.Token);
        await accepted.GetStream().ReadExactlyAsync(new byte[1], setupCts.Token);

        // The user takes the last domain rule away, or adds the first. A connection decided under the last rule went
        // on by it, as one that an exclude took around the VPN: with no domain rule left, the change ended nothing
        domainFilter.Blocks = isLastRule ? [] : rules;

        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        readCts.CancelAfter(TimeSpan.FromSeconds(5));
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
    public async Task A_connection_whose_host_is_reached_as_the_rules_change_carries_nothing()
    {
        var localEndPoint = TestWebServerLocalEps.AllocateFreeTcpEndPoint(TestHelper.TestIps.LocalNsTestIp);
        using var listener = new TcpListener(localEndPoint);
        listener.Start();
        var remoteEndPoint = new IPEndPoint(TestHelper.TestIps.RemoteNsTestIp, localEndPoint.Port);

        await using var server = await TestHelper.CreateServer();
        var ipFilter = new StaticIpFilter(null) {
            ExcludeRanges = new[] { remoteEndPoint.Address }.ToOrderedIpRanges()
        };
        var netFilter = CreateNetFilter(ipFilter,
            new StaticDomainFilter(null) { Blocks = ["a-name-nobody-asks-for.example"] });
        await using var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server)), netFilter: netFilter,
            socketFactory: new RuleChangingSocketFactory(ipFilter, localEndPoint, remoteEndPoint.Address));
        await client.WaitForState(ClientState.Connected, TestCt);

        // The app speaks first, so its connection is decided at once, by the old rules, around the VPN. The change
        // that blocks its address comes as its host is being reached, past the check before it. The host is reached
        // all the same, and the connection was then joined to it: the byte the app sent, read for its name, left
        using var ioCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        ioCts.CancelAfter(TimeSpan.FromSeconds(10));
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(remoteEndPoint, ioCts.Token);
        await tcpClient.GetStream().WriteAsync(new byte[] { 1 }, ioCts.Token);
        using var accepted = await listener.AcceptTcpClientAsync(ioCts.Token);
        try {
            Assert.AreEqual(0, await accepted.GetStream().ReadAsync(new byte[1], ioCts.Token));
        }
        catch (IOException) {
            // ended by a reset
        }
    }

    [TestMethod]
    // the server carries no IPv6: its side blocks the family, or sends it around the VPN
    [DataRow(false, true, SplitUnsupportedIpMode.Block, false, typeof(NetFilterException))]
    [DataRow(false, true, SplitUnsupportedIpMode.Exclude, false, null)]
    // the device's own network carries none, and a split sends the address around the VPN
    [DataRow(true, false, SplitUnsupportedIpMode.Block, true, typeof(PacketDropException))]
    // both carry it: the TCP host takes the connection for its name, whatever the IP filter says
    [DataRow(true, true, SplitUnsupportedIpMode.Block, true, null)]
    public void IPv6_TCP_meets_the_IP_filter_before_the_TCP_host_where_an_end_carries_none(
        bool isIpV6SupportedByServer, bool isIpV6SupportedByClient, SplitUnsupportedIpMode unsupportedIpV6Mode,
        bool isExcludedByIp, Type? expectedException)
    {
        // a domain rule is on
        var address = IPAddress.Parse("2001:db8::1");
        using var domainFilteringService = new DomainFilteringService(
            new StaticDomainFilter(null) { Blocks = ["a-name-nobody-asks-for.example"] }, new EventId(),
            tlsBufferSize: 4096) { IsEnabled = true };
        using var netFilter = new NetFilter {
            IpFilter = new ServerIpFilter(new StaticIpFilter(null) {
                ExcludeRanges = isExcludedByIp ? new[] { address }.ToOrderedIpRanges() : IpRangeOrderedList.Empty
            }) {
                IsIpV6SupportedByServer = isIpV6SupportedByServer,
                UnsupportedIpV6Mode = unsupportedIpV6Mode
            }
        };
        using var tunnel = new Tunnel(TestHelper.CreateTunnelOptions());
        using var forwardingManager = new ForwardingManager(new BindingSocketFactory(new TestSocketFactory()),
            new ForwardingManagerOptions {
                IsPingSupported = false,
                PacketForwarderCallbacks = null,
                UdpTimeout = TransportDefaults.UdpTimeout,
                IcmpTimeout = TransportDefaults.IcmpTimeout,
                MaxUdpClientCount = TransportDefaults.MaxUdpClientCount,
                MaxPingClientCount = TransportDefaults.MaxPingClientCount,
                PacketQueueCapacity = TransportDefaults.PacketForwardingQueueCapacity,
                UdpBufferSize = null,
                LogScope = null
            });
        using var tcpHost = new RecordingTcpHost();
        var packetHandler = new ClientPacketHandler(tunnel, tcpHost, domainFilteringService, netFilter,
            forwardingManager, dnsServers: [], isIpV6SupportedByServer, SplitDnsMode.DefaultRoute,
            new PassthroughState()) {
            UseTcpForwardingIpV4 = true,
            UseTcpForwardingIpV6 = true,
            IsIpV6SupportedByClient = isIpV6SupportedByClient
        };

        // The first packet of an IPv6 connection. Where it cannot go the way its verdict says, it is dropped, so the
        // connection is never made and the app goes on to IPv4: with a domain rule on, the TCP host made the
        // connection for its name, and ended it once the name was read. Where it can, the TCP host takes it
        using var ipV6Packet = PacketBuilder.BuildTcp(IPAddress.Parse("fd00::2"), address,
            sourcePort: 50000, destinationPort: 443, options: [], payload: []);
        if (expectedException == null)
            packetHandler.ProcessOutgoingPacket(ipV6Packet);
        else
            Assert.AreEqual(expectedException,
                Assert.Throws<Exception>(() => packetHandler.ProcessOutgoingPacket(ipV6Packet)).GetType());

        Assert.HasCount(expectedException == null ? 1 : 0, tcpHost.Packets);

        // IPv4 goes to the TCP host for its name
        using var ipV4Packet = PacketBuilder.BuildTcp(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("198.51.100.1"),
            sourcePort: 50000, destinationPort: 443, options: [], payload: []);
        packetHandler.ProcessOutgoingPacket(ipV4Packet);
        Assert.AreSame(ipV4Packet, tcpHost.Packets[^1]);
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
