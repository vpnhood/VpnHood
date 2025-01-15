using VpnHood.Core.Common.Net;

namespace VpnHood.Core.Server;

internal class SessionManagerOptions
{
    public required TimeSpan DeadSessionTimeout { get; init; }
    public required TimeSpan HeartbeatInterval { get; init; }
    public required IpRange TunIpRange { get; init; }
}