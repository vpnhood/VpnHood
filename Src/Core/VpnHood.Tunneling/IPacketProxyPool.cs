using PacketDotNet;

namespace VpnHood.Tunneling;

public interface IPacketProxyPool : IDisposable
{
    public Task SendPacket(IPPacket ipPacket);
    public int ClientCount { get; }
    public int RemoteEndPointCount { get; }
}