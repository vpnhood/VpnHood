using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Proxies;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

internal class ClientPacketHandler(Tunnel tunnel, ClientHost clientHost)
{
    private readonly Tunnel _tunnel = tunnel;
    private readonly ClientHost _clientHost = clientHost;
    public bool IsDnsOverTlsDetected { get; private set; }

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
        if (_clientHost.IsOwnPacket(ipPacket)) {
            _clientHost.ProcessOutgoingPacket(ipPacket, null);
            return;
        }

        // detect DoT
        IsDnsOverTlsDetected |= ipPacket.Protocol is IpProtocol.Tcp && ipPacket.ExtractTcp().DestinationPort == 853;

        // check is the packet in the range. 
        var isInRange = IsInEpRange(ipPacket, forceInRange);
        if (isInRange) {
            if (ipPacket.IsV6() && !IsIpV6SupportedByServer)
                throw new PacketDropException("A protected IPv6 packet is dropped because server can not handle it.");

            // Tcp
            if (ipPacket.Protocol == IpProtocol.Tcp) {
                if (IsTcpProxy)
                    _clientHost.ProcessOutgoingPacket(ipPacket, isInRange);
                else
                    _tunnel.SendPacketQueued(ipPacket);

                return;
            }

            // Udp
            if (ipPacket.Protocol == IpProtocol.Udp) {
                if (!ShouldTunnelUdpPacket(ipPacket.ExtractUdp()))
                    throw new PacketDropException("A UDP packet is dropped because it is blocked by the configuration.");

                _tunnel.SendPacketQueued(ipPacket);
                return;
            }

            throw new PacketDropException("Packet has been dropped because no one handle it.");
        }
        else {
            if (ipPacket.IsV6() && !IsIpV6SupportedByClient)
                throw new PacketDropException("An unprotected IPv6 packet is dropped because client can not handle it.");

            // Tcp
            if (ipPacket.Protocol == IpProtocol.Tcp) {
                _clientHost.ProcessOutgoingPacket(ipPacket, isInRange);
                return;
            }

            // Udp
            if (ipPacket.Protocol == IpProtocol.Udp) {
                _proxyManager.SendPacketQueued(ipPacket);
                return;
            }

            // Icmp is not supported by the local proxy for split tunneling
            if (ipPacket.IsIcmpEcho()) {
                _tunnel.SendPacketQueued(ipPacket);
                return;
            }

            throw new PacketDropException("Packet has been dropped because no one handle it.");
        }
    }

}