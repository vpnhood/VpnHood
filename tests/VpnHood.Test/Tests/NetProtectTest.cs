using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.NetProtection;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Test.Packets;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class NetProtectTest : TestBase
{
    [TestMethod]
    public async Task MaxTcpWaitConnect_reject()
    {
        // create server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.TcpConnectTimeout = TimeSpan.FromSeconds(1);
        fileAccessManagerOptions.SessionOptions.MaxTcpConnectWaitCount = 0;
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token);

        try {
            // a generous timeout: under parallel load the SYN can take seconds to reach the stack,
            // and the fetch must outlive that delay to receive the rejection instead of timing out
            await TestHelper.Test_Https(timeout: TimeSpan.FromSeconds(15));
            Assert.Fail("Exception expected!");
        }
        catch (Exception ex) {
            Assert.AreEqual(nameof(HttpRequestException), ex.GetType().Name);
        }
    }

    [TestMethod]
    public async Task MaxTcpWaitConnect_accept()
    {
        // create server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.TcpConnectTimeout = TimeSpan.FromSeconds(1);
        fileAccessManagerOptions.SessionOptions.MaxTcpConnectWaitCount = 1;
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.ChannelProtocol = ChannelProtocol.Udp;
        clientOptions.UseTcpProxy = true;
        await using var client = await TestHelper.CreateClient(clientOptions);

        await TestHelper.Test_Https();
        await TestHelper.Test_Https();
    }

    [TestMethod]
    public async Task MaxUdpDnsPacketsPerSecond_by_proxy()
    {
        await Test_MaxUdpDnsPacketsPerSecond(vpnAdapter: null);
    }

    [TestMethod]
    public async Task MaxUdpDnsPacketsPerSecond_by_vpn_adapter()
    {
        using var vpnAdapter = new TestUdpServerVpnAdapter();
        var adapterUsed = false;
        vpnAdapter.PacketReceived += (_, _) => adapterUsed = true;

        await Test_MaxUdpDnsPacketsPerSecond(vpnAdapter);
        Assert.IsTrue(adapterUsed);
    }

    [TestMethod]
    public async Task WindowRateLimiter_limits_each_window()
    {
        // a window longer than any uptime, so the test cannot span a window's turn
        var windowRateLimiter = new WindowRateLimiter(3, TimeSpan.FromDays(100_000));
        Assert.IsTrue(windowRateLimiter.TryAcquire());
        Assert.IsTrue(windowRateLimiter.TryAcquire());
        Assert.IsTrue(windowRateLimiter.TryAcquire());
        Assert.IsFalse(windowRateLimiter.TryAcquire());

        // the next window passes again
        var shortRateLimiter = new WindowRateLimiter(1, TimeSpan.FromMilliseconds(200));
        Assert.IsTrue(shortRateLimiter.TryAcquire());
        await AssertEqualsWait(true, shortRateLimiter.TryAcquire);
    }

    [TestMethod]
    public void MaxTcpConnectsPerSecond_by_vpn_adapter()
    {
        using var netProtector = new NetProtector(CreateNetProtectorOptions(maxTcpConnectsPerSecond: 2));

        // a burst of SYNs: a second's worth passes, or two as the burst may span a second's turn
        var passedCount = Enumerable.Range(0, 10)
            .Count(_ => VerifyTcpPacket(netProtector, "10.10.10.1:80", syn: true, ack: false));
        Assert.IsTrue(passedCount is >= 2 and <= 4, $"PassedCount: {passedCount}");

        // the rest of a connection is not counted
        Assert.IsTrue(VerifyTcpPacket(netProtector, "10.10.10.1:80", syn: false, ack: true));
        Assert.IsTrue(VerifyTcpPacket(netProtector, "10.10.10.1:80", syn: true, ack: true));
    }

    [TestMethod]
    public void MaxTcpConnectsPerSecond_shared_by_vpn_adapter_and_proxy()
    {
        // the proxy's connection takes the second's whole budget, so the TUN's SYN after it is refused; a pair that
        // spans a second's turn has two budgets, so it is tried again
        while (true) {
            using var netProtector = new NetProtector(CreateNetProtectorOptions(maxTcpConnectsPerSecond: 1));
            var windowIndex = Environment.TickCount64 / 1000;
            Assert.IsTrue(netProtector.TryAcquireTcpConnect());
            var synPassed = VerifyTcpPacket(netProtector, "10.10.10.1:80", syn: true, ack: false);
            if (Environment.TickCount64 / 1000 != windowIndex)
                continue;

            Assert.IsFalse(synPassed);
            return;
        }
    }

    [TestMethod]
    public void NetScan_by_vpn_adapter()
    {
        using var netProtector = new NetProtector(CreateNetProtectorOptions(netScanLimit: 1));
        Assert.IsTrue(VerifyTcpPacket(netProtector, "10.10.10.1:80", syn: true, ack: false));
        Assert.IsFalse(VerifyTcpPacket(netProtector, "10.10.10.2:80", syn: true, ack: false));
        Assert.AreEqual(1, netProtector.NetScanErrorCount);

        // another network is not affected
        Assert.IsTrue(VerifyTcpPacket(netProtector, "10.10.11.1:80", syn: true, ack: false));
    }

    [TestMethod]
    public void Unreadable_packets_are_dropped()
    {
        using var netProtector = new NetProtector(CreateNetProtectorOptions());
        using var tcpPacket = PacketBuilder.BuildTcp(IPEndPoint.Parse("[2001:db8::1]:50000"),
            IPEndPoint.Parse("[2001:db8::2]:80"), [], []);

        // the IPv6 headers the packet reads through hide nothing
        using var readPacket = IpV6ExtensionHeaderBuilder.Insert(tcpPacket,
            IpV6ExtensionHeaderBuilder.Options(IpProtocol.IPv6HopByHopOptions));
        Assert.IsTrue(netProtector.VerifyReadable(readPacket));

        // one it stops at, behind which the kernel finds TCP all the same
        using var routedPacket = IpV6ExtensionHeaderBuilder.Insert(tcpPacket, IpV6ExtensionHeaderBuilder.Routing());
        Assert.IsFalse(netProtector.VerifyReadable(routedPacket));

        // fragments, which the kernel reassembles
        using var ipV6Fragment = IpV6ExtensionHeaderBuilder.Insert(tcpPacket,
            IpV6ExtensionHeaderBuilder.Fragment(offset: 0, moreFragments: true));
        Assert.IsFalse(netProtector.VerifyReadable(ipV6Fragment));

        using var ipV4Fragment = (IpV4Packet)PacketBuilder.BuildTcp(IPEndPoint.Parse("10.255.0.2:50000"),
            IPEndPoint.Parse("10.10.10.1:80"), [], []);
        ipV4Fragment.FragmentOffset = 1;
        Assert.IsFalse(netProtector.VerifyReadable(ipV4Fragment));
    }

    [TestMethod]
    public void MaxTcpConnectsPerSecond_counts_syns_behind_extension_headers()
    {
        using var netProtector = new NetProtector(CreateNetProtectorOptions(maxTcpConnectsPerSecond: 1));
        using var tcpPacket = PacketBuilder.BuildTcp(IPEndPoint.Parse("[2001:db8::1]:50000"),
            IPEndPoint.Parse("[2001:db8::2]:80"), [], []);
        tcpPacket.ExtractTcp().Synchronize = true;

        // a burst of SYNs behind a Hop-by-Hop header: a second's worth passes, or two as the burst may span a second's
        // turn
        var passedCount = Enumerable.Range(0, 10).Count(_ => {
            using var ipPacket = IpV6ExtensionHeaderBuilder.Insert(tcpPacket,
                IpV6ExtensionHeaderBuilder.Options(IpProtocol.IPv6HopByHopOptions));
            return netProtector.VerifyReadable(ipPacket) &&
                   netProtector.VerifyPacket(ipPacket, ipPacket.Protocol, ipPacket.GetDestinationEndPoint());
        });
        Assert.IsTrue(passedCount is >= 1 and <= 2, $"PassedCount: {passedCount}");
    }

    [TestMethod]
    public async Task MaxTcpConnectsPerSecond_by_proxy()
    {
        // create server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.MaxTcpConnectsPerSecond = 2;
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(TestHelper.CreateClientOptions(token, useTcpProxy: true));

        // connections at once: a second's worth goes, the rest of that second is refused
        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => TestHelper.Test_Https(timeout: TimeSpan.FromSeconds(10), throwError: false)));
        Assert.IsTrue(results.Contains(true));
        Assert.IsTrue(results.Contains(false));
    }

    private async Task Test_MaxUdpDnsPacketsPerSecond(IVpnAdapter? vpnAdapter)
    {
        // create server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.MaxUdpDnsPacketsPerSecond = 5;
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions, vpnAdapter: vpnAdapter);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token);

        // a flood from one port: a few seconds' worth comes back at most, as the burst may span a second's turn
        using var udpClient = new UdpClient(AddressFamily.InterNetwork);
        for (var i = 0; i < 100; i++)
            await udpClient.SendAsync(new byte[10], MockEps.UdpNsEchoEndPoint1, TestCt);

        var echoCount = await CountReceived(udpClient, quietTime: TimeSpan.FromSeconds(2));
        Assert.IsTrue(echoCount is > 0 and <= 25, $"EchoCount: {echoCount}");

        // the rest of UDP is not limited
        await TestHelper.Test_UdpEcho();
    }

    // the datagrams that arrive until none has for the quiet time
    private async Task<int> CountReceived(UdpClient udpClient, TimeSpan quietTime)
    {
        var count = 0;
        while (true) {
            using var quietCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
            quietCts.CancelAfter(quietTime);
            try {
                await udpClient.ReceiveAsync(quietCts.Token);
            }
            catch (OperationCanceledException) {
                TestCt.ThrowIfCancellationRequested();
                return count;
            }

            count++;
        }
    }

    private static NetProtectorOptions CreateNetProtectorOptions(int maxTcpConnectsPerSecond = 1000,
        int? netScanLimit = null)
    {
        return new NetProtectorOptions {
            MaxUdpDnsPacketsPerSecond = 1000,
            MaxTcpConnectsPerSecond = maxTcpConnectsPerSecond,
            NetScanLimit = netScanLimit,
            NetScanTimeout = netScanLimit != null ? TimeSpan.FromSeconds(100) : null,
            LogScope = new LogScope()
        };
    }

    private static bool VerifyTcpPacket(NetProtector netProtector, string destinationEndPoint, bool syn, bool ack)
    {
        using var ipPacket = PacketBuilder.BuildTcp(IPEndPoint.Parse("10.255.0.2:50000"),
            IPEndPoint.Parse(destinationEndPoint), [], []);
        var tcpPacket = ipPacket.ExtractTcp();
        tcpPacket.Synchronize = syn;
        tcpPacket.Acknowledgment = ack;
        return netProtector.VerifyPacket(ipPacket, IpProtocol.Tcp, ipPacket.GetDestinationEndPoint());
    }
}