namespace VpnHood.Core.Client.Abstractions;

public class ProxyServerStatus
{
    public required string Id { get; init; }
    public bool IsActive { get; set; }
    public int Penalty { get; set; }
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
    public TimeSpan Latency { get; set; }
    public DateTime LastUsedTime { get; set; }
    public string? ErrorMessage { get; set; }
}