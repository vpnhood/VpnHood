using VpnHood.Net.Packets;

namespace VpnHood.Net.PacketTransports;

public interface IPacketTransport : IDisposable
{
    // The handler takes each packet: it delivers or disposes it, or throws to give it back to the transport, which
    // disposes it. So one handler: a second could see a packet the first one disposed or handed on
    event EventHandler<IpPacket>? PacketReceived;
    bool IsSending { get; }

    // The transport takes the packet, whatever the outcome: sent, dropped, refused (false) or failed (a throw), it
    // is never the caller's again
    bool SendPacketQueued(IpPacket ipPacket);
    ValueTask SendPacketQueuedAsync(IpPacket ipPacket);
    ReadOnlyPacketTransportStat PacketStat { get; }
}