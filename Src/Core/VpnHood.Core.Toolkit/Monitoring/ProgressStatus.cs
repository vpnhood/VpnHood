namespace VpnHood.Core.Toolkit.Monitoring;

public record struct ProgressStatus(int Completed, int Total, DateTime StartedTime, int Percentage);