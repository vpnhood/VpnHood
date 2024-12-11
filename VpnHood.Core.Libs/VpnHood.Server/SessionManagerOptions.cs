﻿namespace VpnHood.Server;

internal class SessionManagerOptions
{
    public required TimeSpan DeadSessionTimeout { get; init; }
    public required TimeSpan HeartbeatInterval { get; init; }
}