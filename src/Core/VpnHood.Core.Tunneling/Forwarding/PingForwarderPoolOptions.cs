using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Tunneling.Forwarding;

public class PingForwarderPoolOptions
{
    public required IPacketForwarderCallbacks? PacketForwarderCallbacks { get; init; }
    public required TimeSpan IcmpTimeout { get; init; }
    public required int MaxClientCount { get; init; }
    public required LogScope? LogScope { get; init; }
}