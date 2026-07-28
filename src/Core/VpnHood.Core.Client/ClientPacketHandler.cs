using System.Net;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Proxies;

namespace VpnHood.Core.Client;

internal class ClientPacketHandler(
    Tunnel tunnel,
    IClientTcpHost clientTcpHost,
    DomainFilteringService domainFilteringService,
    NetFilter netFilter,
    ProxyManager proxyManager,
    IReadOnlyList<IPAddress> dnsServers,
    bool isIpV6SupportedByServer,
    SplitDnsMode splitDnsMode,
    PassthroughState passthroughState)
{
    public bool IsDnsOverTlsDetected { get; private set; }
    public bool DropQuic { get; set; }
    public bool DropUdp { get; set; }
    public bool UseTcpProxy { get; set; }
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

        // apply net filter
        if (filterAction == FilterAction.Default && netFilter.IpFilter != null)
            filterAction = netFilter.IpFilter.Process(ipPacket.Protocol, destinationEndPoint);

        // block
        if (filterAction == FilterAction.Block)
            throw new NetFilterException("A packet has been dropped by the domain filter.");

        // force by passthrough for ad setting. The packet will be forced to exclude regardless of the domain filter result and net filter result.
        // PassthroughForAd is enabled, DNS packets should go through the tunnel and ad traffic should not go
        // through the tunnel, to prevent trigger red flags on ad providers
        if (ShouldPassthroughForAd(ipPacket))
            filterAction = FilterAction.Exclude;

        // Keep DNS inside the tunnel (SplitDnsMode): a resolver reachable around the VPN leaks every name the
        // user looks up and lets the local network answer for them, so DNS overrides any split that excluded
        // it. Deliberately AFTER the block check — an address the user chose to drop stays dropped — and after
        // the ad passthrough, so a passthrough exemption can never be undone by a later stage.
        // No routability check here: a destination the server does not route is the server's to drop — dead
        // inside the tunnel is a visible failure, and the server may still choose to serve DNS. Either way
        // the query never travels outside.
        if (ShouldForceDnsToTunnel(destinationEndPoint))
            filterAction = FilterAction.Include;

        // force by ICMP echo request. The local proxy can not handle ICMP, so echo requests are forced
        // through the tunnel even when a gate excluded them.
        if (ipPacket.IsIcmpEcho())
            filterAction = FilterAction.Include;

        // detect DoT
        IsDnsOverTlsDetected |= ipPacket.Protocol is IpProtocol.Tcp &&
                                ipPacket.ExtractTcp().DestinationPort == DnsPorts.DnsOverTls;

        // tunnel unless a gate vetoed: Default means "no objection" and stays inside the tunnel (fail-closed)
        if (filterAction is FilterAction.Exclude)
            ProcessOutgoingPacketExclude(ipPacket);
        else // include or default
            ProcessOutgoingPacketInclude(ipPacket);
    }

    private void ProcessOutgoingPacketInclude(IpPacket ipPacket)
    {
        if (ipPacket.IsV6() && !IsIpV6SupportedByServer)
            throw new PacketDropException("A protected IPv6 packet is dropped because server can not handle it.");

        // Tcp
        if (ipPacket.Protocol == IpProtocol.Tcp) {
            if (UseTcpProxy)
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

        if (ipPacket.IsV6() && !IsIpV6SupportedByClient)
            throw new PacketDropException("An unprotected IPv6 packet is dropped because client can not handle it.");

        // Tcp
        if (ipPacket.Protocol == IpProtocol.Tcp) {
            clientTcpHost.ProcessOutgoingPacket(ipPacket);
            return;
        }

        // Udp
        if (ipPacket.Protocol == IpProtocol.Udp) {
            proxyManager.SendPacketQueued(ipPacket);
            return;
        }

        // Icmp is not supported by the local proxy for split tunneling
        if (ipPacket.IsIcmpEcho())
            throw new PacketDropException("An ICMP echo request packet is dropped because it can not be handled by the local proxy.");

        throw new PacketDropException("Packet has been dropped because no one handle it.");
    }


    // Which DNS traffic SplitDnsMode forces through the tunnel. Detection is by destination port — the only
    // DNS signal available at this level — and protocol-agnostic on purpose: 53 is DNS over both udp and tcp,
    // 853 is DoT. DoH is undetectable here, on 443 it is indistinguishable from any other https flow.
    // No LAN exception: whether the local network is captured at all is the device include set's business;
    // any DNS packet that reaches this handler must be tunneled or dropped, never let around the tunnel.
    private bool ShouldForceDnsToTunnel(IpEndPointValue destinationEndPoint)
    {
        return splitDnsMode is SplitDnsMode.IncludeAll && DnsPorts.IsDnsPort(destinationEndPoint.Port);
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

    private bool ShouldPassthroughForAd(IpPacket ipPacket)
    {
        if (!passthroughState.PassthroughForAd)
            return false;

        // Passthrough for ad is enabled, DNS packets should go through the tunnel and ad traffic should not go through the tunnel,
        // but dns packets should not be treated as ad traffic, to make sure we can resolve the domain and by pass regional ad blockers.
        // DNS on ANY transport (udp/tcp 53, DoT, DoQ) is exempt, and so is any packet to a configured resolver.
        var isDnsPacket =
            DnsPorts.IsDnsPort(ipPacket.GetDestinationEndPoint().Port) ||
            dnsServers.Contains(ipPacket.DestinationAddress);

        return !isDnsPacket;
    }
}