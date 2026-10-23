using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.Packets;

// The extension headers between an IPv6 packet's fixed header and its upper-layer header (RFC 8200 4)
internal static class IpV6ExtensionHeaders
{
    // the headers a reader steps over to the upper layer, as Linux does (ipv6_ext_hdr); ESP ends the chain as a
    // protocol of its own, as Mobility, HIP and Shim6 do for Linux
    public static bool IsExtensionHeader(IpProtocol protocol)
    {
        return protocol is IpProtocol.IPv6HopByHopOptions or IpProtocol.IPv6RoutingHeader or
            IpProtocol.IPv6FragmentHeader or IpProtocol.IpSecAuthenticationHeader or
            IpProtocol.IPv6DestinationOptions;
    }

    // The upper layer behind a packet's extension headers, and where it starts. It stops at a header it does not read
    // through, which it returns: a Routing header, as the upper layer's checksum covers the final destination the
    // header names (RFC 8200 8.1), or a header that runs past the packet. A fragment other than the first stops at its
    // Fragment header, as no upper layer follows it
    public static IpProtocol FindUpperLayer(ReadOnlySpan<byte> packet, out int offset, out bool isFragment)
    {
        var protocol = (IpProtocol)packet[6];
        offset = 40;
        isFragment = false;

        // each header takes 8 bytes at least, so the walk ends within the packet
        while (packet.Length >= offset + 8) {
            int length;
            switch (protocol) {
                case IpProtocol.IPv6HopByHopOptions or IpProtocol.IPv6DestinationOptions:
                    length = (packet[offset + 1] + 1) * 8;
                    break;

                case IpProtocol.IpSecAuthenticationHeader:
                    length = (packet[offset + 1] + 2) * 4;
                    break;

                case IpProtocol.IPv6FragmentHeader: {
                    // the offset in the high 13 bits, the more-fragments flag in the lowest
                    var fragmentField = (packet[offset + 2] << 8) | packet[offset + 3];
                    isFragment |= (fragmentField & 0xFFF9) != 0;
                    if (fragmentField >> 3 != 0) {
                        protocol = (IpProtocol)packet[offset];
                        offset += 8;
                        return protocol;
                    }

                    length = 8;
                    break;
                }

                default:
                    return protocol;
            }

            if (packet.Length < offset + length)
                return protocol;

            protocol = (IpProtocol)packet[offset];
            offset += length;
        }

        return protocol;
    }
}
