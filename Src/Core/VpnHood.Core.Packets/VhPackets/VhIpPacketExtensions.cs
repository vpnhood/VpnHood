namespace VpnHood.Core.Packets.VhPackets;

public static class VhIpPacketExtensions
{
    public static VhUdpPacket ExtractUdp(this VhIpPacket ipPacket, bool building = false)
    {
        if (ipPacket.Protocol != VhIpProtocol.Udp)
            throw new InvalidDataException($"Invalid UDP packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new VhUdpPacket(ipPacket.Payload, building);
        return (VhUdpPacket)ipPacket.PayloadPacket;
    }

    public static void UpdateAllChecksums(this VhIpPacket ipPacket)
    {
        if (ipPacket is VhIpV4Packet ipV4Packet)
            ipV4Packet.UpdateHeaderChecksum(ipV4Packet);

        if (ipPacket.Protocol == VhIpProtocol.Udp)
            ipPacket.ExtractUdp().UpdateChecksum(ipPacket);
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
}