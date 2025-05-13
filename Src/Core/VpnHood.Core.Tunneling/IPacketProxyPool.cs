using VpnHood.Core.Packets;
using VpnHood.Core.Packets.VhPackets;

namespace VpnHood.Core.Tunneling;

public interface IPacketProxyPool : IDisposable
{
    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    public void SendPacketQueued(IpPacket ipPacket);
    public int ClientCount { get; }
    public int RemoteEndPointCount { get; }
}