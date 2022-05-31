using System;

namespace VpnHood.Common.Messaging;

public class AccessUsage
{
    public long SentTraffic { get; set; }
    public long ReceivedTraffic { get; set; }
    public long MaxTraffic { get; set; }
    public DateTime? ExpirationTime { get; set; }
    public int MaxClientCount { get; set; }
    public int? ActiveClientCount { get; set; }
}