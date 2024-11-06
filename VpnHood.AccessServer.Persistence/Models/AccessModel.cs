namespace VpnHood.AccessServer.Persistence.Models;

public class AccessBaseModel
{
    public required Guid AccessId { get; set; }
    public required Guid AccessTokenId { get; set; }
    public required Guid? DeviceId { get; set; }
    public required DateTime CreatedTime { get; set; }
    public required DateTime LastUsedTime { get; set; }
    public required DateTime? AdRewardExpirationTime { get; set; }
    public required int? AdRewardMinutes { get; set; }
    public required string? Description { get; set; }
    public required long LastCycleSentTraffic { get; set; }
    public required long LastCycleReceivedTraffic { get; set; }
    public required long LastCycleTraffic { get; set; }
    public required long TotalSentTraffic { get; set; }
    public required long TotalReceivedTraffic { get; set; }
    public required long TotalTraffic { get; set; } //db computed
    public long CycleSentTraffic => TotalSentTraffic - LastCycleSentTraffic;
    public long CycleReceivedTraffic => TotalReceivedTraffic - LastCycleReceivedTraffic;
    public long CycleTraffic { get; set; } //db computed
}

public class AccessModel : AccessBaseModel
{
    public virtual AccessTokenModel? AccessToken { get; set; }
    public virtual DeviceModel? Device { get; set; }
    public virtual ICollection<SessionModel>? Sessions { get; set; }
}