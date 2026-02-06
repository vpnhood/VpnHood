using VpnHood.Core.Packets;

namespace VpnHood.Core.DomainFiltering.SniExtractors;

public interface IPacketSniExtractor
{
    QuicSniResultNew ExtractSni(IpPacket ipPacket);
}

