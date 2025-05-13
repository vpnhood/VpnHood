using VpnHood.Core.Packets;

namespace VpnHood.Core.Tunneling;

public interface IPacketSenderQueued
{
    public void SendPacketQueued(IpPacket ipPacket);
}