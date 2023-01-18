using System.Threading.Tasks;
using PacketDotNet;

namespace VpnHood.Tunneling;

public interface IPacketReceiver
{
    public Task OnPacketReceived(IPPacket packet);
}