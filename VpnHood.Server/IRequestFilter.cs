using PacketDotNet;
using System.Net;

namespace VpnHood.Server;

public interface IRequestFilter
{
    public IPPacket? Process(IPPacket ipPacket);
    public IPEndPoint? Process(ProtocolType protocolType, IPEndPoint requestEndPoint);
}