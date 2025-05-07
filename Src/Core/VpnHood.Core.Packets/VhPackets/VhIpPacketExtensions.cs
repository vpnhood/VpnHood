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


    public static void UpdateAllChecksums(this VhIpPacket ipPacket)
    {
        if (ipPacket is VhIpV4Packet ipV4Packet)
            ipV4Packet.UpdateHeaderChecksum(ipV4Packet);

        if (ipPacket.Protocol == VhIpProtocol.Udp)
            ipPacket.ExtractUdp().UpdateChecksum(ipPacket);

        if (ipPacket.Protocol == VhIpProtocol.Tcp)
            ipPacket.ExtractTcp().UpdateChecksum(ipPacket);

    }


    public static bool IsChecksumValid(this VhUdpPacket udpPacket, VhIpPacket ipPacket)
    {
        return udpPacket
            .IsChecksumValid(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
    }

    public static ushort ComputeChecksum(this VhUdpPacket udpPacket, VhIpPacket ipPacket)
    {
        return udpPacket
            .ComputeChecksum(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
    }

    public static void UpdateChecksum(this VhUdpPacket udpPacket, VhIpPacket ipPacket)
    {
        udpPacket
            .UpdateChecksum(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
    }

    public static bool IsChecksumValid(this VhTcpPacket tcpPacket, VhIpPacket ipPacket)
    {
        return tcpPacket
            .IsChecksumValid(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
    }

    public static ushort ComputeChecksum(this VhTcpPacket tcpPacket, VhIpPacket ipPacket)
    {
        return tcpPacket
            .ComputeChecksum(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
    }

    public static void UpdateChecksum(this VhTcpPacket tcpPacket, VhIpPacket ipPacket)
    {
        tcpPacket
            .UpdateChecksum(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
    }

}