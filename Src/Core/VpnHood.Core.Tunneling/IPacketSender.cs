using VpnHood.Core.Packets.VhPackets;

namespace VpnHood.Core.Tunneling;

public interface IPacketSender
{
    Task SendPacketsAsync(IList<IpPacket> ipPackets);
}