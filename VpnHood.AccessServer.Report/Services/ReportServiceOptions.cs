namespace VpnHood.AccessServer.Report.Services;

public class ReportServiceOptions
{
    public required string ConnectionString { get; init; }
    public required TimeSpan ServerUpdateStatusInterval { get; init; }
}