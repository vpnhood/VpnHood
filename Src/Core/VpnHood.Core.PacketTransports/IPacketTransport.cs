using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.PacketTransports;

public interface IPacketTransport : IDisposable
{
    event EventHandler<IpPacket>? PacketReceived;
    bool IsSending { get; }
    bool SendPacketQueued(IpPacket ipPacket);
    ValueTask SendPacketQueuedAsync(IpPacket ipPacket);
    ReadOnlyPacketTransportStat PacketStat { get; }
}