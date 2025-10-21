namespace VpnHood.Core.Toolkit.Jobs;

public class JobOptions
{
    public static TimeSpan DefaultInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan Interval { get; init; } = DefaultInterval;
    public string? Name { get; init; }
    public TimeSpan? DueTime { get; init; }
    public int? MaxRetry { get; init; }
    public bool AutoStart { get; init; } = true;
}