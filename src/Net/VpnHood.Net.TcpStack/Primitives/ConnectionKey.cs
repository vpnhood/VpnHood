using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.TcpStack.Primitives;

/// <summary>A connection's key in the stack's table: the segment's addresses as values read from the packet, and its
/// ports, so the receive path finds the connection with no object made. A value carries its IP version, which keeps an
/// IPv6 flow apart from an IPv4 one whose addresses read as the same numbers (::10.0.0.2 and 10.0.0.2), and hashes each
/// 32-bit word on its own, so a sender cannot pick addresses that collide in the table whatever the seed.</summary>
internal readonly record struct ConnectionKey(
    IpAddressValue SourceAddress,
    IpAddressValue DestinationAddress,
    ushort SourcePort,
    ushort DestinationPort)
{
    public static ConnectionKey From(IpPacket ipPacket, TcpPacket tcpPacket) => new(
        ipPacket.SourceAddressValue, ipPacket.DestinationAddressValue, tcpPacket.SourcePort, tcpPacket.DestinationPort);
}
