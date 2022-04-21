using System;
using System.Collections.Generic;

namespace VpnHood.AccessServer.Test;

public class TestFillData
{
    public DateTime StartedTime { get; set; } = DateTime.UtcNow;
    public Api.UsageInfo ItemUsageInfo { get; set; } = new()
    {
        ReceivedTraffic = 1000,
        SentTraffic = 500
    };
    public List<Api.AccessToken> AccessTokens { get; set; } = new();
    public List<Api.SessionResponseEx> SessionResponses { get; set; } = new();
    public List<Api.SessionRequestEx> SessionRequests { get; set; } = new();
}