using VpnHood.Core.Packets;

namespace VpnHood.Core.Client;

public interface ITcpStreamAssembler
{
    bool IsOwnPacket(IpPacket ipPacket);
    public void ProcessOutgoingPacket(IpPacket ipPacket);
}