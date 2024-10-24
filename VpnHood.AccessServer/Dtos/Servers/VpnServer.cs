using VpnHood.AccessServer.Dtos.AccessPoints;
using VpnHood.AccessServer.Persistence.Enums;

namespace VpnHood.AccessServer.Dtos.Servers;

public class VpnServer
{
    public required Guid ServerId { get; init; }
    public required string ServerName { get; init; }
    public required Guid ServerFarmId { get; init; }
    public required string? ServerFarmName { get; set; }
    public required Location? Location { get; init; }
    public required string? Version { get; init; }
    public required string? EnvironmentVersion { get; init; }
    public required string? OsInfo { get; init; }
    public required string? MachineName { get; init; }
    public required long? TotalMemory { get; init; }
    public required int? TotalSwapMemoryMb { get; init; }
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
    public required bool AllowInAutoLocation { get; init; }
    public required Uri? HostPanelUrl { get; init; }
    public required int? Power { get; init; }
    public required string? PublicIpV4 { get; set; }
    public required string? PublicIpV6 { get; set; }
    public required string[] Tags { get; set; }
    public required string? ClientFilterId { get; set; }
    public required string? ClientFilterName { get; set; }
    public required int? ConfigSwapMemorySizeMb { get; set; }
}