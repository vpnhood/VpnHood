using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Client;

public interface ITcpStreamAssembler
{
    bool IsOwnPacket(IpPacket ipPacket);
    public void ProcessOutgoingPacket(IpPacket ipPacket);
}