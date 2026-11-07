using VpnHood.Net.PacketTransports;

namespace VpnHood.Core.Tunneling.Forwarding;

public interface IPacketForwarderPool : IPacketTransport
{
    public int ClientCount { get; }
    public int RemoteEndPointCount { get; }
}