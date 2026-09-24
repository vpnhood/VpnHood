namespace VpnHood.Net.Toolkit.Monitoring;

// ReSharper disable once NotAccessedPositionalProperty.Global
public record struct ProgressStatus(int Completed, int Total, DateTime StartedTime, int Percentage);