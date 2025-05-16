namespace VpnHood.Core.Packets.Transports;

public interface IPacketTransport : IDisposable
{
    event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    bool IsSending { get; }
    DateTime LastSentTime { get; }
    DateTime LastReceivedTime { get; }
    bool SendPacketQueued(IpPacket ipPacket);
    ValueTask SendPacketQueuedAsync(IpPacket ipPacket);
}