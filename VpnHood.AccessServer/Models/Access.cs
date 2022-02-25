using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Models;

public class Access
{
    public Guid AccessId { get; set; }
    public Guid AccessTokenId { get; set; }
    public Guid? DeviceId { get; set; }
    public DateTime CreatedTime { get; set; }
    public DateTime? EndTime { get; set; }
    public DateTime? LockedTime { get; set; }
    public string? Description { get; set; }

    public long CycleSentTraffic { get; set; }
    public long CycleReceivedTraffic { get; set; }
    public long CycleTraffic { get; set; }
    public long TotalSentTraffic { get; set; }
    public long TotalReceivedTraffic { get; set; }
    public long TotalTraffic { get; set; }
    public DateTime AccessedTime { get; set; } = DateTime.UtcNow;

    public virtual AccessToken? AccessToken { get; set; }
    public virtual Device? Device { get; set; }
    [JsonIgnore] public virtual ICollection<AccessUsageEx>? AccessUsages { get; set; }
}