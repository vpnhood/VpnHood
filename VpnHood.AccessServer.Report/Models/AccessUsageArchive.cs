namespace VpnHood.AccessServer.Report.Models;

public class AccessUsageArchive
{
    public required long AccessUsageId { get; set; }
    public required Guid AccessId { get; init; }
    public required long SessionId { get; init; }
    public required Guid ServerId { get; init; }
    public required long SentTraffic { get; set; }
    public required long ReceivedTraffic { get; set; }
    public required long LastCycleSentTraffic { get; set; }
    public required long LastCycleReceivedTraffic { get; set; }
    public required long TotalSentTraffic { get; set; }
    public required long TotalReceivedTraffic { get; set; }
    public required DateTime CreatedTime { get; set; }
    public long CycleSentTraffic => TotalSentTraffic - LastCycleSentTraffic;
    public long CycleReceivedTraffic => TotalReceivedTraffic - LastCycleReceivedTraffic;
    public required Guid ProjectId { get; init; } // Denormal
    public required Guid AccessTokenId { get; init; } // Denormal
    public required Guid ServerFarmId { get; init; } // Denormal
    public required Guid DeviceId { get; init; } // Denormal
}