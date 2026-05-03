using System.Net;
using VpnHood.Core.Packets;

namespace VpnHood.Core.Client;

internal interface IClientTcpHost : IDisposable
{
    IReadOnlyList<IPAddress> CatcherAddressIps { get; }
    event EventHandler<IpPacket>? PacketReceived;
    bool IsOwnPacket(IpPacket ipPacket);
    void ProcessOutgoingPacket(IpPacket ipPacket);
    void DropCurrentConnections();
    void Start();
}
