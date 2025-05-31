using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Tunneling.Proxies;

public class PingProxyPoolOptions
{
    public required IPacketProxyCallbacks? PacketProxyCallbacks { get; init; }
    public required TimeSpan IcmpTimeout { get; init; } 
    public required int MaxClientCount { get; init; }
    public required bool AutoDisposePackets { get; init; }
    public required LogScope? LogScope { get; init; }
}