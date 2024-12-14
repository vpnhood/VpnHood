using PacketDotNet;

namespace VpnHood.Core.Tunneling;

public interface IPacketReceiver
{
    public Task OnPacketReceived(IPPacket packet);
}