using VpnHood.Core.Packets;

namespace VpnHood.Core.PacketTransports;

public interface IPacketTransport : IDisposable
{
    event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    bool IsSending { get; }
    DateTime LastSentTime { get; }
    DateTime LastReceivedTime { get; }
    bool SendPacketQueued(IpPacket ipPacket);
    ValueTask SendPacketQueuedAsync(IpPacket ipPacket);
}