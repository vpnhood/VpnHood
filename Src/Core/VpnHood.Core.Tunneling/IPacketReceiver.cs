using VpnHood.Core.Packets.VhPackets;

namespace VpnHood.Core.Tunneling;

public interface IPacketReceiver
{
    public void OnPacketReceived(IpPacket packet);
}