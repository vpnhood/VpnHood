using System;
using VpnHood.AccessServer.Models;
using VpnHood.Server;
using VpnHood.Server.Messaging;
using System.Collections.Generic;

namespace VpnHood.AccessServer.Test;

public class TestFillData
{
    public DateTime StartedTime { get; set; } = DateTime.UtcNow;
    public UsageInfo ItemUsageInfo { get; set; } = new()
    {
        ReceivedTraffic = 1000,
        SentTraffic = 500
    };
    public List<AccessToken> AccessTokens { get; set; } = new();
    public List<SessionResponseEx> SessionResponses { get; set; } = new();
    public List<SessionRequestEx> SessionRequests { get; set; } = new();
}