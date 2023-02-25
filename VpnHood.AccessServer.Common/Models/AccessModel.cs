namespace VpnHood.AccessServer.Models;

public class AccessModel : ICloneable
{
    public Guid AccessId { get; set; }
    public Guid AccessTokenId { get; set; }
    public Guid? DeviceId { get; set; }
    public DateTime CreatedTime { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedTime { get; set; } = DateTime.UtcNow;
    public string? Description { get; set; }

    public long LastCycleSentTraffic { get; set; }
    public long LastCycleReceivedTraffic { get; set; }
    public long LastCycleTraffic { get; set; }
    public long TotalSentTraffic { get; set; }
    public long TotalReceivedTraffic { get; set; }
    public long TotalTraffic { get; set; } //db computed
    public long CycleSentTraffic => TotalSentTraffic - LastCycleSentTraffic;
    public long CycleReceivedTraffic => TotalReceivedTraffic - LastCycleReceivedTraffic;
    public long CycleTraffic { get; set; } //db computed

    public virtual AccessTokenModel? AccessToken { get; set; }
    public virtual DeviceModel? Device { get; set; }

    public object Clone()
    {
        return new AccessModel
        {
            AccessId = AccessId,
            DeviceId = DeviceId,
            AccessTokenId = AccessTokenId,
            CreatedTime = CreatedTime,
            LastUsedTime = LastUsedTime,
            CycleTraffic = CycleTraffic,
            LastCycleReceivedTraffic = LastCycleReceivedTraffic,
            LastCycleSentTraffic = LastCycleSentTraffic,
            LastCycleTraffic = LastCycleTraffic,
            TotalSentTraffic = TotalSentTraffic,
            TotalReceivedTraffic = TotalReceivedTraffic,
            TotalTraffic = TotalTraffic,
            Description = Description,
        };
    }
}