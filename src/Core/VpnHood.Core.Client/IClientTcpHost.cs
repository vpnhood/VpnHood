using VpnHood.Core.Packets;

namespace VpnHood.Core.Client;

internal interface IClientTcpHost : IDisposable
{
    event EventHandler<IpPacket>? PacketReceived;
    void ProcessOutgoingPacket(IpPacket ipPacket);
    void DropCurrentConnections();
    void Start();
}