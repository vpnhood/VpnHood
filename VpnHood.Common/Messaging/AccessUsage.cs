using System.Text.Json.Serialization;

namespace VpnHood.Common.Messaging;

public class AccessUsage
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPremium { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Traffic Traffic { get; set; } = new();
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long MaxTraffic { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime? ExpirationTime { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int MaxClientCount { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? ActiveClientCount { get; set; }
}