using System;

namespace VpnHood.AccessServer.Dtos;

public class Access
{
    public Guid AccessId { get; set; }
    public Guid AccessTokenId { get; set; }
    public DateTime? EndTime { get; set; }
    public DateTime? LockedTime { get; set; }
    public DateTime CreatedTime { get; set; }
    public DateTime AccessedTime { get; set; }
    public string? Description { get; set; }

    public long CycleSentTraffic { get; set; }
    public long CycleReceivedTraffic { get; set; }
    public long CycleTraffic { get; set; }
    public long TotalSentTraffic { get; set; }
    public long TotalReceivedTraffic { get; set; }
    public long TotalTraffic { get; set; }

    public long CurCycleSentTraffic => TotalSentTraffic - CycleSentTraffic;
    public long CurCycleReceivedTraffic => TotalReceivedTraffic - CycleReceivedTraffic;
    public long CurCycleTraffic => CurCycleSentTraffic + CurCycleReceivedTraffic;
}