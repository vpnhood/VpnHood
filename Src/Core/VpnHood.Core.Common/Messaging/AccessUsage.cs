
using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Messaging;

public class AccessUsage
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CanExtendByRewardedAd { get; set; }

    public bool IsPremium { get; set; }  // session premium

    public Traffic Traffic { get; set; } = new(); // cycle traffic
    public Traffic TotalTraffic { get; set; } = new(); // total traffic

    public long MaxTraffic { get; set; } // session max traffic

    public DateTime? ExpirationTime { get; set; } // It is the session expiration not access expiration

    [Obsolete("v5.0.636 or upper. Use AccessInfo in hello response")] 
    public int MaxClientCount { get; set; }
    
    public int? ActiveClientCount { get; set; }
}