namespace VpnHood.AccessServer.Persistence.Caches;

public class ProjectCache
{
    public required Guid ProjectId { get; init; }
    public required string? GaMeasurementId { get; init; }
    public required string? GaApiSecret { get; init; }
}