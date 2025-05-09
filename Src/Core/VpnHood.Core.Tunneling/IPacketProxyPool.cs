using VpnHood.Core.Packets.VhPackets;

namespace VpnHood.Core.Tunneling;

public interface IPacketProxyPool : IDisposable
{
    public Task SendPacket(IpPacket ipPacket);
    public int ClientCount { get; }
    public int RemoteEndPointCount { get; }
}