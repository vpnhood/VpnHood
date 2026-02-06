using VpnHood.Core.Packets;

namespace VpnHood.Core.DomainFiltering.SniExtractors;

public interface IPacketSniExtractor
{
    PacketSniResult ExtractSni(IpPacket ipPacket);
}

