using System.Net;
using VpnHood.Core.DomainFiltering;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Net.Extensions;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Proxies;

namespace VpnHood.Core.Client;

internal class ClientPacketHandler(
    Tunnel tunnel,
    ClientHost clientHost,
    DomainFilteringService domainFilteringService,
    NetFilter netFilter,
    ProxyManager proxyManager)
{
    public IPAddress[] DnsServers { get; set; } = [];
    public bool PassthroughForAd { get; set; }
    public bool IsDnsOverTlsDetected { get; private set; }
    public bool DropQuic { get; set; }
    public bool DropUdp { get; set; }
    public bool UseTcpProxy { get; set; }
    public bool IsIpV6SupportedByClient { get; set; }
    public bool IsIpV6SupportedByServer { get; set; }

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
        // mapper
        if (netFilter.IpMapper?.ToHost(ipPacket.Protocol, ipPacket.GetDestinationEndPoint(), out var newEndPoint) == true) {
            ipPacket.SetDestinationEndPoint(newEndPoint);
            ipPacket.UpdateAllChecksums();
        }

        // apply net filter
        if (filterAction == FilterAction.Default && netFilter.IpFilter != null)
            filterAction = netFilter.IpFilter.Process(ipPacket.Protocol, ipPacket.GetDestinationEndPoint());

        // Multicast packets are not supported (Already excluded by adapter filter)
        if (ipPacket.IsMulticast()) //todo: move to ipFilter
            throw new PacketDropException("A multicast packet has been dropped.");

        // Broadcast packets are not supported (Already excluded by adapter filter)
        if (ipPacket.IsBroadcast()) //todo: move to ipFilter
            throw new PacketDropException("A broadcast packet has been dropped.");

        // TcpHost has to manage its own packets
        if (clientHost.IsOwnPacket(ipPacket)) {
            clientHost.ProcessOutgoingPacket(ipPacket, null);
            return;
        }

        // block
        if (filterAction == FilterAction.Block)
            throw new PacketDropException("A packet has been dropped by the domain filter.");

        // force by passthrough for ad setting. The packet will be forced to exclude regardless of the domain filter result and net filter result.
        // PassthroughForAd is enabled, DNS packets should go through the tunnel and ad traffic should not go 
        // through the tunnel, to prevent trigger red flags on ad providers
        if (ShouldPassthroughForAd(ipPacket))
            filterAction = FilterAction.Exclude;

        // detect DoT
        IsDnsOverTlsDetected |= ipPacket.Protocol is IpProtocol.Tcp && ipPacket.ExtractTcp().DestinationPort == 853;

        // check is the packet in the range. 
        if (filterAction is FilterAction.Include)
            ProcessOutgoingPacketInclude(ipPacket);
        else // default or exclude
            ProcessOutgoingPacketExclude(ipPacket);
    }

    private void ProcessOutgoingPacketInclude(IpPacket ipPacket)
    {
        if (ipPacket.IsV6() && !IsIpV6SupportedByServer)
            throw new PacketDropException("A protected IPv6 packet is dropped because server can not handle it.");

        // Tcp
        if (ipPacket.Protocol == IpProtocol.Tcp) {
            if (UseTcpProxy)
                clientHost.ProcessOutgoingPacket(ipPacket, true);
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
        if (ipPacket.IsV6() && !IsIpV6SupportedByClient)
            throw new PacketDropException("An unprotected IPv6 packet is dropped because client can not handle it.");

        // Tcp
        if (ipPacket.Protocol == IpProtocol.Tcp) {
            clientHost.ProcessOutgoingPacket(ipPacket, false);
            return;
        }

        // Udp
        if (ipPacket.Protocol == IpProtocol.Udp) {
            proxyManager.SendPacketQueued(ipPacket);
            return;
        }

        // Icmp is not supported by the local proxy for split tunneling
        if (ipPacket.IsIcmpEcho()) {
            tunnel.SendPacketQueued(ipPacket);
            return;
        }

        throw new PacketDropException("Packet has been dropped because no one handle it.");
    }


    private bool ShouldDropUdpPacket(UdpPacket udpPacket)
    {
        if (DropUdp) 
            return true;

        return DropQuic && udpPacket.DestinationPort is 80 or 443;
    }

    private bool ShouldPassthroughForAd(IpPacket ipPacket)
    {
        if (!PassthroughForAd) return false;
        return ipPacket.GetDestinationEndPoint().Port is 53 or 853 ||
               DnsServers.Contains(ipPacket.DestinationAddress);
    }
}