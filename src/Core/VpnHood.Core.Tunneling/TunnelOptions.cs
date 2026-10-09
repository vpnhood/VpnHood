using VpnHood.Core.Common.Configuration;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Tunneling;

public class TunnelOptions
{
    public required int MaxPacketChannelCount { get; init; } = 8;
    public required int Mtu { get; init; }

    // The session's speed limit, bytes per second each way; 0 means none. A send limit gives the tunnel a
    // queue of its own, which paces all the session's packets
    public Traffic MaxSpeed { get; init; }
    public Traffic MaxSpeedBurst { get; init; } = new(TransportDefaults.MaxSpeedBurst, TransportDefaults.MaxSpeedBurst);
}