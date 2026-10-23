using System.Buffers.Binary;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Test.Packets;

// IPv6 extension headers for tests, each as its type and its bytes; the packet they go into fills in their first byte,
// the next header
public static class IpV6ExtensionHeaderBuilder
{
    // Hop-by-Hop or Destination Options, with one PadN option; a length of more units than the bytes runs past
    public static (IpProtocol Type, byte[] Bytes) Options(IpProtocol type, byte length = 0)
    {
        return (type, [0, length, 1, 4, 0, 0, 0, 0]);
    }

    public static (IpProtocol Type, byte[] Bytes) Routing()
    {
        return (IpProtocol.IPv6RoutingHeader, [0, 0, 4, 0, 0, 0, 0, 0]);
    }

    public static (IpProtocol Type, byte[] Bytes) Authentication()
    {
        return (IpProtocol.IpSecAuthenticationHeader, [0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1]);
    }

    // the offset in 8-byte units
    public static (IpProtocol Type, byte[] Bytes) Fragment(int offset, bool moreFragments)
    {
        var field = (offset << 3) | (moreFragments ? 1 : 0);
        return (IpProtocol.IPv6FragmentHeader, [0, 0, (byte)(field >> 8), (byte)field, 0, 0, 0, 7]);
    }

    // a copy of the packet with the headers between its fixed header and its upper layer
    public static IpPacket Insert(IpPacket ipPacket, params (IpProtocol Type, byte[] Bytes)[] headers)
    {
        var buffer = ipPacket.Buffer.ToArray();
        var extensionHeaders = new List<byte>();
        for (var i = 0; i < headers.Length; i++) {
            var bytes = headers[i].Bytes.ToArray();
            bytes[0] = i + 1 < headers.Length ? (byte)headers[i + 1].Type : buffer[6];
            extensionHeaders.AddRange(bytes);
        }

        var packet = buffer[..40].Concat(extensionHeaders).Concat(buffer[40..]).ToArray();
        packet[6] = (byte)headers[0].Type;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), (ushort)(packet.Length - 40));
        return PacketBuilder.Parse(packet);
    }
}
