namespace VpnHood.AccessServer.Caches;

public class ProjectCache
{
    public required Guid ProjectId { get; init; }
    public required string? GaMeasurementId { get; init; }
    public required string? GaApiSecret { get; init; }
}