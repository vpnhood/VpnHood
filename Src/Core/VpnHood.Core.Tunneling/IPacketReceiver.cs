using PacketDotNet;

namespace VpnHood.Core.Tunneling;

public interface IPacketReceiver
{
    public void OnPacketReceived(IPPacket packet);
}