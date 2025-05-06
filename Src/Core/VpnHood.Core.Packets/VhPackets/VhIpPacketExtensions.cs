namespace VpnHood.Core.Packets.VhPackets;

public static class VhIpPacketExtensions
{
    public static VhUdpPacket ExtractUdpPacket(this VhIpPacket ipPacket)
    {
        if (ipPacket.Protocol != VhIpProtocol.Udp)
            throw new InvalidDataException($"Invalid UDP packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new VhUdpPacket(ipPacket.Payload);
        return (VhUdpPacket)ipPacket.PayloadPacket;
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

    public static ushort UpdateChecksum(this VhUdpPacket udpPacket, VhIpPacket ipPacket)
    {
        return udpPacket
            .UpdateChecksum(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
    }
}