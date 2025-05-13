using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Tunneling;

public class PingProxyPoolOptions
{
    public required IPacketProxyCallbacks? PacketProxyCallbacks { get; init; }
    public required TimeSpan IcmpTimeout { get; init; } 
    public required int MaxClientCount { get; init; }
    public required bool AutoDisposeSentPackets { get; init; }
    public required LogScope? LogScope { get; init; }
}