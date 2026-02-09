using System.Net;
using VpnHood.Core.DomainFiltering;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Proxies;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

internal class ClientPacketHandler(
    Tunnel tunnel,
    ClientHost clientHost,
    DomainFilteringService domainFilteringService,
    IpFilterHandler ipFilterHandler,
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

        if (domainFilteringService.IsEnabled)
            ProcessOutgoingPacketWithDomainFilter(ipPacket);
        else
            ProcessOutgoingPacket(ipPacket, null);
    }

    private void ProcessOutgoingPacketWithDomainFilter(IpPacket ipPacket)
    {
        // process domain filtering
        var result = domainFilteringService.ProcessPacket(ipPacket);
        if (result.NeedMore)
            return;

        // block packet if the result is block. The packet and all pending packets will be disposed in this case.
        if (result.Action == DomainFilterAction.Block) {
            foreach (var blockedPacket in result.Packets)
                blockedPacket.Dispose();
            ipPacket.Dispose();
            return;
        }

        // determine forceInRange based on the filter action to override the normal IP range check for these packets. 
        bool? forceInRange = result.Action switch {
            DomainFilterAction.Include => true,
            DomainFilterAction.Exclude => false,
            _ => null
        };

        // flush pending packets if there are any. 
        foreach (var pendingPackets in result.Packets)
            ProcessOutgoingPacket(pendingPackets, forceInRange);

        // process the current packet
        ProcessOutgoingPacket(ipPacket, forceInRange);
    }

    // WARNING: Performance Critical! Mango Section
    private void ProcessOutgoingPacket(IpPacket ipPacket, bool? forceInRange)
    {
        // Multicast packets are not supported (Already excluded by adapter filter)
        if (ipPacket.IsMulticast()) {
            PacketLogger.LogPacket(ipPacket, "A multicast packet has been dropped.");
            ipPacket.Dispose();
            return;
        }

        // Broadcast packets are not supported (Already excluded by adapter filter)
        if (ipPacket.IsBroadcast()) {
            PacketLogger.LogPacket(ipPacket, "A broad packet has been dropped.");
            ipPacket.Dispose();
            return;
        }

        // TcpHost has to manage its own packets
        if (clientHost.IsOwnPacket(ipPacket)) {
            clientHost.ProcessOutgoingPacket(ipPacket, null);
            return;
        }

        // detect DoT
        IsDnsOverTlsDetected |= ipPacket.Protocol is IpProtocol.Tcp && ipPacket.ExtractTcp().DestinationPort == 853;

        // check is the packet in the range. 
        var isInRange = IsInEpRange(ipPacket, forceInRange);
        if (isInRange)
            ProcessOutgoingPacketInclude(ipPacket);
        else
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
            if (!ShouldTunnelUdpPacket(ipPacket.ExtractUdp()))
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


    private bool ShouldTunnelUdpPacket(UdpPacket udpPacket)
    {
        if (DropUdp)
            return false;

        if (udpPacket.DestinationPort is 80 or 443 && DropQuic)
            return false;

        return true;
    }

    private bool IsInEpRange(IpPacket ipPacket, bool? force)
    {
        var destinationPort = 0;
        if (ipPacket.Protocol == IpProtocol.Tcp) destinationPort = ipPacket.ExtractTcp().DestinationPort;
        if (ipPacket.Protocol == IpProtocol.Udp) destinationPort = ipPacket.ExtractUdp().DestinationPort;
        return IsInEpRange(ipPacket.DestinationAddress, destinationPort, force);
    }

    private bool IsInEpRange(IPAddress ipAddress, int port, bool? force)
    {
        // PassthroughForAd is enabled, DNS packets should go through the tunnel and ad traffic should not go 
        // through the tunnel, to prevent trigger red flags on ad providers
        if (PassthroughForAd)
            return port is 53 or 853 || DnsServers.Contains(ipAddress);

        // force is not null means the domain filter has determined the packet should be included or excluded,
        // so we can skip the normal IP range check.
        if (force != null)
            return force.Value;

        // normal IP range check
        return ipFilterHandler.IsInIpRange(ipAddress);
    }
}