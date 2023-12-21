namespace VpnHood.Common.JobController;

public class JobConfig
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan? DueTime { get; init; }
    public string? Name { get; init; }
}