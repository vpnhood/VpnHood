using VpnHood.Core.DomainFiltering.SniExtractors;
using VpnHood.Core.Packets;

namespace VpnHood.Core.DomainFiltering;


/// <summary>
/// Get the packet and store the source endpoint result.
/// It uses its own TimeoutDictionary to store the result for next.
/// It uses IpEndPointValue to prevent heap allocation for each packet.
/// </summary>
public class PacketSniService(
    DomainFilterResolver domainFilterResolver, 
    IPacketSniExtractor sniExtractor,
    TimeSpan connectionTimeout)
{

    public PacketFilterResult ProcessPacket(IpPacket ipPacket)
    {
        sniExtractor.ExtractSni(ipPacket);
    }
}
