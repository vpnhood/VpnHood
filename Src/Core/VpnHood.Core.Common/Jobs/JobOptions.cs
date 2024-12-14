namespace VpnHood.Core.Common.Jobs;

public class JobOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan? DueTime { get; init; }
    public string? Name { get; set; }
}