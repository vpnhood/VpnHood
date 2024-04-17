namespace VpnHood.AccessServer.Persistence.Caches;

public class ProjectCache
{
    public required Guid ProjectId { get; init; }
    public required string? ProjectName { get; set; }
    public required string? GaMeasurementId { get; init; }
    public required string? GaApiSecret { get; init; }
    public required string GoogleAdSecret { get; init; }
}