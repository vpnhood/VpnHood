using System.Buffers.Binary;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.TcpStack.Primitives;

/// <summary>A connection's key in the stack's table: the segment's addresses as numbers read from the packet, its ports
/// and its IP version, so the receive path finds the connection without the IPAddress objects of an endpoint pair (one
/// per segment, two with IPv6). The version keeps an IPv6 flow apart from an IPv4 one whose addresses read as the same
/// numbers (::10.0.0.2 and 10.0.0.2).</summary>
internal readonly record struct ConnectionKey(
    UInt128 SourceAddress,
    UInt128 DestinationAddress,
    ushort SourcePort,
    ushort DestinationPort,
    IpVersion Version)
{
    public static ConnectionKey From(IpPacket ipPacket, TcpPacket tcpPacket) => new(
        ReadAddress(ipPacket.SourceAddressSpan), ReadAddress(ipPacket.DestinationAddressSpan),
        tcpPacket.SourcePort, tcpPacket.DestinationPort, ipPacket.Version);

    private static UInt128 ReadAddress(ReadOnlySpan<byte> address) => address.Length == 4
        ? BinaryPrimitives.ReadUInt32BigEndian(address)
        : BinaryPrimitives.ReadUInt128BigEndian(address);
}
