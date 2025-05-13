using VpnHood.Core.Packets;

namespace VpnHood.Core.Tunneling;

public interface IPacketProxyPool : IPacketSenderQueued, IDisposable
{
    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    public int ClientCount { get; }
    public int RemoteEndPointCount { get; }
}