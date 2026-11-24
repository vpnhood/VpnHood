using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Ads;
using VpnHood.Core.Common.Configuration;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Forwarding;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.PacketTransports;
using VpnHood.Test.Providers;
using SplitDnsMode = VpnHood.Core.Client.Abstractions.SplitDnsMode;

namespace VpnHood.Test.Tests;

// How the client drops the UDP its settings block: QUIC alone, or all UDP but DNS, by one flag each for both IP
// versions, which the session sets where TCP forwarding is chosen (ClientServerTest)
[TestClass]
public class ClientUdpDropTest : TestBase
{
    // A packet of either version meets the same flags; QUIC is ports 80 and 443, which a drop of all UDP takes too, and
    // DNS is never dropped
    [TestMethod]
    [DataRow(true, false)]
    [DataRow(true, true)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public async Task Both_IP_versions_drop_the_UDP_the_flags_block(bool dropQuic, bool dropUdp)
    {
        using var domainFilteringService = new DomainFilteringService(new StaticDomainFilter(null), new EventId(),
            tlsBufferSize: 4096);

        using var netFilter = new NetFilter();
        // the tunnel's UDP channel takes every packet the tunnel is given, and keeps it
        var udpChannel = new RecordingPacketChannel();
        using var tunnel = new Tunnel(TestHelper.CreateTunnelOptions(udpChannel)) { UseUdp = true };
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

        using var tcpHost = new NullTcpHost();
        var packetHandler = new ClientPacketHandler(tunnel, tcpHost, domainFilteringService, netFilter,
            forwardingManager, dnsServers: [], isIpV6SupportedByServer: true, SplitDnsMode.DefaultRoute,
            new PassthroughState()) {
            IsIpV6SupportedByClient = true,
            DropQuic = dropQuic,
            DropUdp = dropUdp
        };

        // each version's QUIC and the rest of its UDP by the flags, but DNS
        var isQuicDropped = dropQuic || dropUdp;
        foreach (var destination in new[] { "198.51.100.1", "2001:db8::1" }) {
            await AssertUdpPacket(packetHandler, udpChannel, destination, 443, isDropped: isQuicDropped);
            await AssertUdpPacket(packetHandler, udpChannel, destination, 80, isDropped: isQuicDropped);
            await AssertUdpPacket(packetHandler, udpChannel, destination, 5000, isDropped: dropUdp);
            await AssertUdpPacket(packetHandler, udpChannel, destination, 53, isDropped: false);
        }

        var passedPerVersion = (isQuicDropped ? 0 : 2) + (dropUdp ? 0 : 1) + 1;
        Assert.AreEqual(passedPerVersion * 2, udpChannel.Packets.Count, "A packet reached the tunnel's channel twice.");
    }

    // A UDP packet the device sends: dropped, or taken into the tunnel, which hands it to its channel. The channel is
    // asked last for one that was dropped, after one that went, so a dropped one that went too would be seen
    private async Task AssertUdpPacket(ClientPacketHandler packetHandler, RecordingPacketChannel udpChannel,
        string destination, int port, bool isDropped)
    {
        var destinationAddress = IPAddress.Parse(destination);
        var sourceAddress = IPAddress.Parse(
            destinationAddress.AddressFamily == AddressFamily.InterNetworkV6 ? "fd00::2" : "10.0.0.2");

        var ipPacket = PacketBuilder.BuildUdp(sourceAddress, destinationAddress, 40000, port, new byte[16]);
        ipPacket.UpdateAllChecksums();
        if (!isDropped) {
            packetHandler.ProcessOutgoingPacket(ipPacket);
            await AssertEqualsWait(true, () => udpChannel.Packets.Any(x => x.GetDestinationEndPoint().Port == port &&
                    x.GetDestinationEndPoint().Address.Equals(destinationAddress)),
                $"the UDP packet to {destination}:{port} in the tunnel");

            return;
        }

        using var droppedPacket = ipPacket;
        Assert.ThrowsExactly<PacketDropException>(() => packetHandler.ProcessOutgoingPacket(droppedPacket),
            $"A UDP packet to {destination}:{port} passed.");

        Assert.IsFalse(udpChannel.Packets.Any(x => x.GetDestinationEndPoint().Port == port &&
                                                     x.GetDestinationEndPoint().Address.Equals(destinationAddress)),
            $"The dropped UDP packet to {destination}:{port} reached the tunnel.");
    }

    // a tunnel's UDP channel that keeps each packet it is given
    private sealed class RecordingPacketChannel : IPacketChannel
    {
        private readonly ConcurrentQueue<IpPacket> _packets = new();

        public IReadOnlyCollection<IpPacket> Packets => _packets;
        public event EventHandler<IpPacket>? PacketReceived {
            add { }
            remove { }
        }

        public bool IsSending => false;
        public ReadOnlyPacketTransportStat PacketStat { get; } = new(new PacketTransportStat());
        public string ChannelId => "recording";
        public DateTime LastActivityTime => DateTime.UtcNow;
        public Traffic Traffic => new();
        public PacketChannelState State => PacketChannelState.Connected;

        public bool SendPacketQueued(IpPacket ipPacket)
        {
            _packets.Enqueue(ipPacket);
            return true;
        }

        public ValueTask SendPacketQueuedAsync(IpPacket ipPacket)
        {
            _packets.Enqueue(ipPacket);
            return default;
        }

        public void Start()
        {
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => default;
    }

    // a TCP host that takes nothing: no TCP packet comes here
    private sealed class NullTcpHost : IClientTcpHost
    {
        public event EventHandler<IpPacket>? PacketReceived {
            add { }
            remove { }
        }

        public void ProcessOutgoingPacket(IpPacket ipPacket) =>
            throw new InvalidOperationException("A TCP packet came to a test of UDP.");

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
}
