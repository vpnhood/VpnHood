namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions;

public class ProxyEndPointStatus
{
    public int Penalty { get; set; }
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
    public TimeSpan? Latency { get; set; }
    public DateTime? LastUsedTime { get; set; }
    public string? ErrorMessage { get; set; }
    public StatusQuality Quality {
        get {
            return Penalty switch {
                <= 0 when SucceededCount is 0 && FailedCount is 0 => StatusQuality.Unknown,
                <= 0 when SucceededCount > 0 => StatusQuality.Excellent,
                <= 10 when SucceededCount > 0 => StatusQuality.Good,
                <= 20 when SucceededCount > 0 => StatusQuality.Fair,
                <= 100 when SucceededCount > 0 => StatusQuality.Poor,
                <= 10000 when SucceededCount > 0 => StatusQuality.VeryPoor,
                _ => StatusQuality.Failed
            };
        }
    }
}