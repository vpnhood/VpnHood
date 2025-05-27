namespace VpnHood.Core.Toolkit.Jobs;

public class VhJobOptions
{
    public required TimeSpan Period { get; init; }
    public string? Name { get; init; }
    public TimeSpan? DueTime { get; init; }
    public int? MaxRetry { get; init; }
}