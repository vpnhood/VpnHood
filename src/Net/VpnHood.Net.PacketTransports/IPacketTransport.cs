using VpnHood.Net.Packets;

namespace VpnHood.Net.PacketTransports;

public interface IPacketTransport : IDisposable
{
    event EventHandler<IpPacket>? PacketReceived;
    bool IsSending { get; }
    bool SendPacketQueued(IpPacket ipPacket);
    ValueTask SendPacketQueuedAsync(IpPacket ipPacket);
    ReadOnlyPacketTransportStat PacketStat { get; }
}