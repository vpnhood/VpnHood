namespace VpnHood.Common.JobController;

public class JobOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan? DueTime { get; init; }
    public string? Name { get; init; }
}