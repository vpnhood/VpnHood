namespace VpnHood.Net.Toolkit.Monitoring;

// ReSharper disable once NotAccessedPositionalProperty.Global
/// <summary>Represents the progress of a run of tasks.</summary>
/// <param name="Completed">The number of tasks completed.</param>
/// <param name="Total">The number of tasks.</param>
/// <param name="StartedTime">When the run started, in UTC.</param>
/// <param name="Percentage">The progress, as a percentage.</param>
public record struct ProgressStatus(int Completed, int Total, DateTime StartedTime, int Percentage);