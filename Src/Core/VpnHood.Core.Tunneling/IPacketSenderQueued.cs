using VpnHood.Core.Packets.VhPackets;

namespace VpnHood.Core.Tunneling;

public interface IPacketSenderQueued
{
    public void SendPacketQueued(IpPacket ipPacket);
}