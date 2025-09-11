namespace VpnHood.Core.Client.Abstractions;

public record struct ProgressStatus(int Completed, int Total, DateTime StartedTime, int Percentage);