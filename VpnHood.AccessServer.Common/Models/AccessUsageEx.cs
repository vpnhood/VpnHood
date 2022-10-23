namespace VpnHood.AccessServer.Models;

public class AccessUsageEx
{
    public long AccessUsageId { get; set; }
    public Guid AccessId { get; set; }
    public long SessionId { get; set; }
    public Guid ServerId { get; set; }
    public long SentTraffic { get; set; }
    public long ReceivedTraffic { get; set; }
    public long CycleSentTraffic { get; set; }
    public long CycleReceivedTraffic { get; set; }
    public long TotalSentTraffic { get; set; }
    public long TotalReceivedTraffic { get; set; }
    public DateTime CreatedTime { get; set; }

    public long CurCycleSentTraffic => TotalSentTraffic - CycleSentTraffic;
    public long CurCycleReceivedTraffic => TotalReceivedTraffic - CycleReceivedTraffic;

    // Denormal
    public Guid ProjectId { get; set; }
    public Guid AccessTokenId { get; set; }
    public Guid AccessPointGroupId { get; set; }
    public Guid DeviceId { get; set; }

    public virtual Access? Access { get; set; }
    public virtual Session? Session { get; set; }
    public virtual Server? Server { get; set; }
    public virtual Device? Device { get; set; }
    public virtual Project? Project { get; set; }
    public virtual AccessPointGroup? AccessPointGroup { get; set; }
    public virtual AccessToken? AccessToken { get; set; }
}