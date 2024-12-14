
using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Messaging;

public class AccessUsage
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CanExtendByRewardedAd { get; set; }

    public bool IsPremium { get; set; } 

    public Traffic Traffic { get; set; } = new();
    
    public long MaxTraffic { get; set; }
    
    public DateTime? ExpirationTime { get; set; }
    
    public int MaxClientCount { get; set; }
    
    public int? ActiveClientCount { get; set; }
}