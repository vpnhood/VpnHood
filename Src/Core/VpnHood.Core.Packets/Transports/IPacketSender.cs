namespace VpnHood.Core.Packets.Transports;

public interface IPacketSender : IDisposable
{
    bool IsSending { get; }
    DateTime LastSentTime { get; }
    bool SendPacketQueued(IpPacket ipPacket);
    ValueTask SendPacketQueuedAsync(IpPacket ipPacket);
}