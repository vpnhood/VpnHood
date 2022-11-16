namespace VpnHood.AccessServer.Models;

public class AccessUsageModel
{
    public long AccessUsageId { get; set; }
    public Guid AccessId { get; set; }
    public long SessionId { get; set; }
    public Guid ServerId { get; set; }
    public long SentTraffic { get; set; }
    public long ReceivedTraffic { get; set; }
    public long LastCycleSentTraffic { get; set; }
    public long LastCycleReceivedTraffic { get; set; }
    public long TotalSentTraffic { get; set; }
    public long TotalReceivedTraffic { get; set; }
    public DateTime CreatedTime { get; set; }

    public long CycleSentTraffic => TotalSentTraffic - LastCycleSentTraffic;
    public long CycleReceivedTraffic => TotalReceivedTraffic - LastCycleReceivedTraffic;

    // Denormal
    public Guid ProjectId { get; set; }
    public Guid AccessTokenId { get; set; }
    public Guid AccessPointGroupId { get; set; }
    public Guid DeviceId { get; set; }

    public virtual AccessModel? Access { get; set; }
    public virtual SessionModel? Session { get; set; }
    public virtual ServerModel? Server { get; set; }
    public virtual DeviceModel? Device { get; set; }
    public virtual ProjectModel? Project { get; set; }
    public virtual AccessPointGroupModel? AccessPointGroup { get; set; }
    public virtual AccessTokenModel? AccessToken { get; set; }
}