using PacketDotNet;
using System.Net;

namespace VpnHood.Server;

public interface INetFilter
{
    public IPPacket? ProcessRequest(IPPacket ipPacket);
    public IPEndPoint? ProcessRequest(ProtocolType protocol, IPEndPoint requestEndPoint);
    public IPPacket ProcessReply(IPPacket ipPacket);
}