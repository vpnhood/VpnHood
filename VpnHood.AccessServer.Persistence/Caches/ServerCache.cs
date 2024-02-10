using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.Persistence.Caches;

public class ServerCache
{
    public required Guid ProjectId { get; init; }
    public required Guid ServerId { get; init; }
    public required Guid ServerFarmId { get; init; }
    public required Guid ServerProfileId { get; set; }
    public required string? ServerFarmName { get; init; }
    public required string? ServerName { get; init; }
    public required string? Version { get; init; }
    public required string? LastConfigError { get; init; }
    public required Guid? LastConfigCode { get; init; }
    public required Guid ConfigCode { get; init; }
    public required DateTime? ConfigureTime { get; init; }
    public required bool IsEnabled { get; init; }
    public required Guid AuthorizationCode { get; init; }
    public required AccessPointModel[] AccessPoints { get; init; }
    public required ServerStatusBaseModel? ServerStatus { get; set; }
}