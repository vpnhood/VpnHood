namespace VpnHood.Common.Messaging;

public class AccessUsage
{
    public Traffic Traffic { get; set; } = new();
    public long MaxTraffic { get; set; }
    public DateTime? ExpirationTime { get; set; }
    public int MaxClientCount { get; set; }
    public int? ActiveClientCount { get; set; }
}