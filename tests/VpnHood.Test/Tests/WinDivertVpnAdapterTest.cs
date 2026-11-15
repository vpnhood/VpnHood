using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Test.Device;

namespace VpnHood.Test.Tests;

[TestClass]
public class WinDivertVpnAdapterTest : TestBase
{
    private static readonly IPAddress VpnDnsServer = IPAddress.Parse("10.255.0.1");
    private static readonly IPEndPoint DnsClient = new(IPAddress.Parse("10.255.0.2"), 5353);

    // The DNS rewrite alone, on an adapter that is never started: a capture with SimulateDns takes every DNS query
    // of the machine, the other tests' among them
    private static async Task<TestVpnAdapter> CreateDnsAdapter()
    {
        var adapter = new TestVpnAdapter(new TestVpnAdapterOptions { SimulateDns = true });
        await adapter.SetDnsServersForTest([VpnDnsServer]);
        return adapter;
    }

    // a UDP packet whose payload begins as a DNS message does, with its ID
    private static IpPacket BuildDnsMessage(IPEndPoint source, IPEndPoint destination, ushort queryId)
    {
        var payload = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(payload, queryId);
        var packet = PacketBuilder.BuildUdp(source, destination, payload);
        packet.UpdateAllChecksums();
        return packet;
    }

    // A datagram's first fragment, parsed from its bytes as a captured packet is: a packet from the builder already
    // has its UDP part, which nothing then takes out of the fragment again
    private static IpPacket BuildUdpFragment(IPEndPoint source, IPEndPoint destination)
    {
        using var packet = PacketBuilder.BuildUdp(source, destination, new byte[64]);
        ((IpV4Packet)packet).MoreFragments = true;
        packet.UpdateAllChecksums();
        return PacketBuilder.Parse(packet.Buffer.Span);
    }

    [TestMethod]
    public async Task Queries_from_one_port_get_their_answers_from_the_servers_they_were_sent_to()
    {
        using var adapter = await CreateDnsAdapter();
        var server1 = IPAddress.Parse("198.51.100.1");
        var server2 = IPAddress.Parse("198.51.100.2");

        // one socket asks two servers, each query with an ID of its own; both go to the VPN's server instead
        using var query1 = BuildDnsMessage(DnsClient, new IPEndPoint(server1, 53), queryId: 1);
        using var query2 = BuildDnsMessage(DnsClient, new IPEndPoint(server2, 53), queryId: 2);
        adapter.RewriteDns(query1, read: true);
        adapter.RewriteDns(query2, read: true);
        Assert.AreEqual(VpnDnsServer, query1.DestinationAddress);
        Assert.AreEqual(VpnDnsServer, query2.DestinationAddress);

        // each answer comes back from the server its query was sent to, where both came back from the later one's
        using var answer1 = BuildDnsMessage(new IPEndPoint(VpnDnsServer, 53), DnsClient, queryId: 1);
        using var answer2 = BuildDnsMessage(new IPEndPoint(VpnDnsServer, 53), DnsClient, queryId: 2);
        adapter.RewriteDns(answer1, read: false);
        adapter.RewriteDns(answer2, read: false);
        Assert.AreEqual(server1, answer1.SourceAddress);
        Assert.AreEqual(server2, answer2.SourceAddress);
    }

    [TestMethod]
    public async Task A_UDP_fragment_passes_the_DNS_rewrite_as_it_is()
    {
        using var adapter = await CreateDnsAdapter();
        var server = IPAddress.Parse("198.51.100.1");

        // a datagram's first fragment, out and in: it holds only a piece of its UDP packet, so the rewrite, which
        // took every UDP packet apart before it looked at its port, refused it
        using var outbound = BuildUdpFragment(DnsClient, new IPEndPoint(server, 4000));
        adapter.RewriteDns(outbound, read: true);
        Assert.AreEqual(server, outbound.DestinationAddress);

        using var inbound = BuildUdpFragment(new IPEndPoint(server, 4000), DnsClient);
        adapter.RewriteDns(inbound, read: false);
        Assert.AreEqual(server, inbound.SourceAddress);
    }

    [TestMethod]
    public async Task An_adapter_with_nothing_to_capture_starts()
    {
        using var adapter = TestHelper.CreateTestVpnAdapter();

        // every route is a local network's, which the capture leaves out: no route is left, and WinDivert refused
        // the filter that was made of none
        await adapter.Start(new VpnAdapterOptions {
            SessionName = null,
            VirtualIpNetworkV4 = IpNetwork.Parse("10.255.0.2/24"),
            IncludeNetworks = [IpNetwork.Parse("192.168.0.0/16")],
            DnsServers = []
        }, TestCt);
        Assert.IsTrue(adapter.IsStarted);
    }

    [TestMethod]
    public async Task A_captured_fragment_keeps_its_datagrams_checksum()
    {
        using var adapter = TestHelper.CreateTestVpnAdapter();

        // started with nothing to capture: only a started adapter has the address a captured packet's source becomes
        await adapter.Start(new VpnAdapterOptions {
            SessionName = null,
            VirtualIpNetworkV4 = IpNetwork.Parse("10.255.0.2/24"),
            IncludeNetworks = [IpNetwork.Parse("192.168.0.0/16")],
            DnsServers = []
        }, TestCt);

        // a UDP datagram's first fragment as the host sends it, from the host's own address; here it holds the
        // whole datagram, so clearing its flag puts the datagram together
        using var datagram = (IpV4Packet)PacketBuilder.BuildUdp(new IPEndPoint(IPAddress.Parse("192.0.2.10"), 5353),
            new IPEndPoint(IPAddress.Parse("198.51.100.1"), 4000), new byte[64]);
        datagram.UpdateAllChecksums();
        datagram.MoreFragments = true;
        using var fragment = (IpV4Packet)PacketBuilder.Parse(datagram.Buffer.Span);
        adapter.RewriteCaptured(fragment);

        // Its source is the adapter's now, which the datagram's checksum covers, and a fragment cannot compute that
        // checksum again: left as it was, the datagram failed it at the far end
        Assert.AreEqual(IPAddress.Parse("10.255.0.2"), fragment.SourceAddress);
        fragment.MoreFragments = false;
        using var whole = PacketBuilder.Parse(fragment.Buffer.Span);
        Assert.IsTrue(whole.ExtractUdp().IsChecksumValid(whole));
    }

    [TestMethod]
    public async Task A_new_run_does_not_write_with_the_last_runs_capture_header()
    {
        using var adapter = TestHelper.CreateTestVpnAdapter();
        var remoteEndPoint = new IPEndPoint(TestHelper.TestIps.RemoteTestIps[0], 5000);
        var options = new VpnAdapterOptions {
            SessionName = null,
            VirtualIpNetworkV4 = IpNetwork.Parse("10.255.0.2/24"),
            IncludeNetworks = [new IpNetwork(remoteEndPoint.Address)],
            DnsServers = []
        };

        // a run captures a packet, whose header its writes then reuse
        var capturedCount = 0;
        adapter.PacketReceived += (_, ipPacket) => {
            Interlocked.Increment(ref capturedCount);
            ipPacket.Dispose();
        };
        await adapter.Start(options, TestCt);
        using var udpClient = new UdpClient(AddressFamily.InterNetwork);
        await udpClient.SendAsync(new byte[16], remoteEndPoint, TestCt);
        await AssertEqualsWait(1, () => capturedCount);

        // The next run has captured nothing yet, so it has no header to write with, and the write fails: it is a
        // drop. With the last run's header kept, the packet went out on that run's interface.
        adapter.Stop();
        await adapter.Start(options, TestCt);
        var answer = PacketBuilder.BuildUdp(remoteEndPoint, new IPEndPoint(IPAddress.Parse("10.255.0.2"), 5001), new byte[16]);
        answer.UpdateAllChecksums();
        adapter.SendPacketQueued(answer);
        await AssertEqualsWait(1L, () => adapter.PacketStat.DroppedPackets);
        Assert.AreEqual(0L, adapter.PacketStat.SentPackets);
    }
}
