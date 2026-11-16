using VpnHood.Net.PacketTransports;

namespace VpnHood.Core.Tunneling.Forwarding;

internal interface IPacketForwarderPool : IPacketTransport
{
    public int ClientCount { get; }
    public int RemoteEndPointCount { get; }
}