namespace VpnHood.Core.Packets.VhPackets;

public static class VhIpPacketExtensions
{
    public static VhUdpPacket ExtractUdp(this VhIpPacket ipPacket)
    {
        if (ipPacket.Protocol != VhIpProtocol.Udp)
            throw new InvalidDataException($"Invalid UDP packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new VhUdpPacket(ipPacket.Payload, false);
        return (VhUdpPacket)ipPacket.PayloadPacket;
    }

    public static VhUdpPacket BuildUdp(this VhIpPacket ipPacket)
    {
        if (ipPacket.Protocol != VhIpProtocol.Udp)
            throw new InvalidDataException($"Invalid UDP packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new VhUdpPacket(ipPacket.Payload, true);
        return (VhUdpPacket)ipPacket.PayloadPacket;
    }

    public static VhTcpPacket ExtractTcp(this VhIpPacket ipPacket)
    {
        if (ipPacket.Protocol != VhIpProtocol.Tcp)
            throw new InvalidDataException($"Invalid TCP packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new VhTcpPacket(ipPacket.Payload);
        return (VhTcpPacket)ipPacket.PayloadPacket;
    }

    public static VhTcpPacket BuildTcp(this VhIpPacket ipPacket, int optionsLength = 0)
    {
        if (ipPacket.Protocol != VhIpProtocol.Tcp)
            throw new InvalidDataException($"Invalid TCP packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new VhTcpPacket(ipPacket.Payload, optionsLength);
        return (VhTcpPacket)ipPacket.PayloadPacket;
    }

    public static VhIcmpV4Packet BuildIcmpV4(this VhIpPacket ipPacket)
    {
        if (ipPacket.Protocol != VhIpProtocol.IcmpV4)
            throw new InvalidDataException($"Invalid IcmpV4 packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new VhIcmpV4Packet(ipPacket.Payload, true);
        return (VhIcmpV4Packet)ipPacket.PayloadPacket;
    }

    public static VhIcmpV4Packet ExtractIcmpV4(this VhIpPacket ipPacket)
    {
        if (ipPacket.Protocol != VhIpProtocol.IcmpV4)
            throw new InvalidDataException($"Invalid IcmpV4 packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new VhIcmpV4Packet(ipPacket.Payload, false);
        return (VhIcmpV4Packet)ipPacket.PayloadPacket;
    }

    public static VhIcmpV6Packet BuildIcmpV6(this VhIpPacket ipPacket)
    {
        if (ipPacket.Protocol != VhIpProtocol.IcmpV6)
            throw new InvalidDataException($"Invalid IcmpV6 packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new VhIcmpV6Packet(ipPacket.Payload, true);
        return (VhIcmpV6Packet)ipPacket.PayloadPacket;
    }

    public static VhIcmpV6Packet ExtractIcmpV6(this VhIpPacket ipPacket)
    {
        if (ipPacket.Protocol != VhIpProtocol.IcmpV6)
            throw new InvalidDataException($"Invalid IcmpV6 packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new VhIcmpV6Packet(ipPacket.Payload, false);
        return (VhIcmpV6Packet)ipPacket.PayloadPacket;
    }

    public static void UpdateAllChecksums(this VhIpPacket ipPacket)
    {
        if (ipPacket is VhIpV4Packet ipV4Packet)
            ipV4Packet.UpdateHeaderChecksum(ipV4Packet);

        if (ipPacket.Protocol == VhIpProtocol.Udp)
            ipPacket.ExtractUdp().UpdateChecksum(ipPacket);

        if (ipPacket.Protocol == VhIpProtocol.Tcp)
            ipPacket.ExtractTcp().UpdateChecksum(ipPacket);

        if (ipPacket.Protocol == VhIpProtocol.IcmpV4)
            ipPacket.ExtractIcmpV4().UpdateChecksum(ipPacket);

        if (ipPacket.Protocol == VhIpProtocol.IcmpV6)
            ipPacket.ExtractIcmpV6().UpdateChecksum(ipPacket);

    }

    public static bool IsChecksumValid(this IChecksumPayloadPacket payloadPacket, VhIpPacket ipPacket)
    {
        return payloadPacket
            .IsChecksumValid(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
    }

    public static ushort ComputeChecksum(this IChecksumPayloadPacket payloadPacket, VhIpPacket ipPacket)
    {
        return payloadPacket
            .ComputeChecksum(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
    }

    public static void UpdateChecksum(this IChecksumPayloadPacket payloadPacket, VhIpPacket ipPacket)
    {
        payloadPacket
            .UpdateChecksum(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
    }

}