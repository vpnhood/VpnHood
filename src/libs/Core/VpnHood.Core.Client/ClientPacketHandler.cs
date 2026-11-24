using System.Net;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Ads;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Forwarding;

namespace VpnHood.Core.Client;

internal class ClientPacketHandler(
    Tunnel tunnel,
    IClientTcpHost clientTcpHost,
    DomainFilteringService domainFilteringService,
    NetFilter netFilter,
    ForwardingManager forwardingManager,
    IReadOnlyList<IPAddress> dnsServers,
    bool isIpV6SupportedByServer,
    SplitDnsMode splitDnsMode,
    PassthroughState passthroughState)
{
    // the DNS servers as values, for the check of each packet
    private readonly IpAddressValue[] _dnsServers = dnsServers.Select(x => x.ToValue()).ToArray();

    public bool IsSecureDnsDetected { get; private set; }
    public bool DropQuic { get; set; }
    public bool DropUdp { get; set; }
    public bool UseTcpForwardingIpV4 { get; set; }
    public bool UseTcpForwardingIpV6 { get; set; }
    public bool IsIpV6SupportedByClient { get; set; }
    public bool IsIpV6SupportedByServer => isIpV6SupportedByServer;

    public void ProcessOutgoingPacket(IpPacket ipPacket)
    {
        if (ipPacket.Protocol is IpProtocol.Udp && domainFilteringService.IsEnabled)
            ProcessOutgoingPacketWithDomainFilter(ipPacket);
        else
            ProcessOutgoingPacket(ipPacket, FilterAction.Default);
    }

    private void ProcessOutgoingPacketWithDomainFilter(IpPacket ipPacket)
    {
        // process domain filtering
        var result = domainFilteringService.ProcessPacket(ipPacket);
        if (result.NeedMore)
            return;

        // block packet if the result is block. The packet and all pending packets will be disposed in this case.
        if (result.Action == FilterAction.Block) {
            foreach (var blockedPacket in result.Packets)
                blockedPacket.Dispose();
            ipPacket.Dispose();
            return;
        }

        // flush pending packets if there are any. 
        foreach (var pendingPackets in result.Packets)
            ProcessOutgoingPacket(pendingPackets, result.Action);

        // process the current packet
        ProcessOutgoingPacket(ipPacket, result.Action);
    }

    // WARNING: Performance Critical! Mango Section
    private void ProcessOutgoingPacket(IpPacket ipPacket, FilterAction filterAction)
    {
        var destinationEndPoint = ipPacket.GetDestinationEndPoint();

        // A TCP connection that the TCP host takes while a domain rule is on is decided once its name is read
        // (ClientStreamHandler), by the same filters and overrides in the same order: decided here, an IP block
        // dropped it before the domain rule that includes it was asked, where a QUIC flow's name is asked first, and
        // the documented design has a domain include go past every IP gate.
        // IPv6 that the server or the device's own network does not carry stays with the IP filter below, as before a
        // domain rule: where it cannot go the way the filter says, into the tunnel or around it, its packets are
        // dropped, so its connection is never made, and an app that tries its addresses as it connects goes on to
        // IPv4. Made by the TCP host and ended once its name was read, it would be an error to any app that tries no
        // other address once a connection is made. The price is that a name's verdict passes no IP rule there
        if (ipPacket.Protocol == IpProtocol.Tcp && domainFilteringService.IsEnabled &&
            (ipPacket.IsV6
                ? UseTcpForwardingIpV6 && IsIpV6SupportedByServer && IsIpV6SupportedByClient
                : UseTcpForwardingIpV4)) {
            IsSecureDnsDetected |= DnsPorts.IsSecureDns(ipPacket.Protocol, destinationEndPoint.Port);
            clientTcpHost.ProcessOutgoingPacket(ipPacket);
            return;
        }

        // apply net filter
        if (filterAction == FilterAction.Default && netFilter.IpFilter != null)
            filterAction = netFilter.IpFilter.Process(ipPacket.Protocol, destinationEndPoint);

        // block
        if (filterAction == FilterAction.Block)
            throw new NetFilterException("A packet has been dropped by the filter.");

        // an ad's passthrough, and DNS kept inside, as the stream path has them
        filterAction = ClientFilterOverrides.Apply(filterAction, destinationEndPoint, passthroughState.PassthroughForAd,
            _dnsServers, splitDnsMode);

        // force by ICMP echo request. The forwarding manager can not handle ICMP, so echo requests are forced
        // through the tunnel even when a gate excluded them.
        if (ipPacket.IsIcmpEcho())
            filterAction = FilterAction.Include;

        // detect secure DNS (DoT/DoQ)
        IsSecureDnsDetected |= DnsPorts.IsSecureDns(ipPacket.Protocol, destinationEndPoint.Port);

        // tunnel unless a gate vetoed: Default means "no objection" and stays inside the tunnel (fail-closed)
        if (filterAction is FilterAction.Exclude)
            ProcessOutgoingPacketExclude(ipPacket);
        else // include or default
            ProcessOutgoingPacketInclude(ipPacket);
    }

    private void ProcessOutgoingPacketInclude(IpPacket ipPacket)
    {
        // Tcp, by the version's own forwarding choice; a branch per version, so no packet pays a check more
        if (ipPacket.IsV6) {
            if (!IsIpV6SupportedByServer)
                throw new PacketDropException("A protected IPv6 packet is dropped because server can not handle it.");

            if (ipPacket.Protocol == IpProtocol.Tcp) {
                if (UseTcpForwardingIpV6)
                    clientTcpHost.ProcessOutgoingPacket(ipPacket);
                else
                    tunnel.SendPacketQueued(ipPacket);

                return;
            }
        }
        else if (ipPacket.Protocol == IpProtocol.Tcp) {
            if (UseTcpForwardingIpV4)
                clientTcpHost.ProcessOutgoingPacket(ipPacket);
            else
                tunnel.SendPacketQueued(ipPacket);

            return;
        }

        // Udp
        if (ipPacket.Protocol == IpProtocol.Udp) {
            if (ShouldDropUdpPacket(ipPacket.ExtractUdp()))
                throw new PacketDropException("A UDP packet is dropped because it is blocked by the configuration.");

            tunnel.SendPacketQueued(ipPacket);
            return;
        }

        // Ping
        if (ipPacket.IsIcmpEcho()) {
            tunnel.SendPacketQueued(ipPacket);
            return;
        }

        throw new PacketDropException("Packet has been dropped because no one handle it.");
    }

    private void ProcessOutgoingPacketExclude(IpPacket ipPacket)
    {
        // For exclude, TCP will resemble as stream and it has its own mapper 
        if (ipPacket.Protocol != IpProtocol.Tcp &&
            netFilter.IpMapper?.ToHost(ipPacket.Protocol, ipPacket.GetDestinationEndPoint(), out var newEndPoint) == true) {
            ipPacket.SetDestinationEndPoint(newEndPoint);
            ipPacket.UpdateAllChecksums();
        }

        if (ipPacket.IsV6 && !IsIpV6SupportedByClient)
            throw new PacketDropException("An unprotected IPv6 packet is dropped because client can not handle it.");

        // Tcp
        if (ipPacket.Protocol == IpProtocol.Tcp) {
            clientTcpHost.ProcessOutgoingPacket(ipPacket);
            return;
        }

        // Udp
        if (ipPacket.Protocol == IpProtocol.Udp) {
            forwardingManager.SendPacketQueued(ipPacket);
            return;
        }

        // Icmp is not supported by the forwarding manager for split tunneling
        if (ipPacket.IsIcmpEcho())
            throw new PacketDropException("An ICMP echo request packet is dropped because it can not be handled by the forwarding manager.");

        throw new PacketDropException("Packet has been dropped because no one handle it.");
    }


    private bool ShouldDropUdpPacket(UdpPacket udpPacket)
    {
        // Always allow DNS packets, even if DropUdp is enabled, to make sure we can resolve the domain and by pass regional ad blockers
        if (DnsPorts.IsDnsPort(udpPacket.DestinationPort))
            return false;

        if (DropUdp)
            return true;

        return DropQuic && udpPacket.DestinationPort is 80 or 443;
    }
}