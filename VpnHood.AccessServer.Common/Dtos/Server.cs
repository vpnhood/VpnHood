namespace VpnHood.AccessServer.Dtos;

public class Server
{
    public required Guid ServerId { get; init; }
    public required Guid? AccessPointGroupId { get; init; }
    public required string? AccessPointGroupName { get; init; }
    public required string? Version { get; init; }
    public required string? ServerName { get; init; }
    public required string? EnvironmentVersion { get; init; }
    public required string? OsInfo { get; init; }
    public required string? MachineName { get; init; }
    public required long? TotalMemory { get; init; }
    public required int? LogicalCoreCount { get; init; }
    public required DateTime? ConfigureTime { get; init; }
    public required DateTime CreatedTime { get; init; }
    public required bool AutoConfigure { get; init; }
    public required bool IsEnabled { get; init; }
    public required string? Description { get; init; }
    public required string? LastConfigError { get; init; }
    public required ServerState ServerState { get; set; }
    public required ServerStatusEx? ServerStatus { get; set; }
    public required AccessPoint[] AccessPoints { get; init; }

}