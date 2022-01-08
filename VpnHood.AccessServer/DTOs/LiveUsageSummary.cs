﻿namespace VpnHood.AccessServer.DTOs;

public class LiveUsageSummary
{
    public int TotalServerCount { get; set; }
    public long UsingBandwidth { get; set; }
    public int ActiveServerCount { get; set; }
    public int IdleServerCount { get; set; }
    public int LostServerCount { get; set; }
    public int NotInstalledServerCount { get; set; }
    public int SessionCount { get; set; }
    public long TunnelSendSpeed { get; set; }
    public long TunnelReceiveSpeed { get; set; }
}