namespace VpnHood.AccessServer.Persistence.Models;

public class ServerModel
{
    public required Guid ProjectId { get; init; }
    public required Guid ServerId { get; init; }
    public required string ServerName { get; set; }
    public required string? Version { get; set; } 
    public required string? EnvironmentVersion { get; set; }
    public required string? OsInfo { get; set; }
    public required string? MachineName { get; set; }
    public required long? TotalMemory { get; set; }
    public required int? LogicalCoreCount { get; set; }
    public required DateTime? ConfigureTime { get; set; }
    public required DateTime CreatedTime { get; set; }
    public required bool IsEnabled { get; set; }
    public required string? Description { get; set; }
    public required Guid AuthorizationCode { get; set; }
    public required byte[] ManagementSecret { get; set; } 
    public required Guid ServerFarmId { get; set; }
    public required Guid ConfigCode { get; set; }
    public required Guid? LastConfigCode { get; set; }
    public required string? LastConfigError { get; set; }
    public required bool AutoConfigure { get; set; }
    public required int? LocationId { get; set; }
    public required bool IsDeleted { get; set; }
    public required string? GatewayIpV4 { get; set; }
    public required string? GatewayIpV6 { get; set; }
    public required bool AllowInAutoLocation { get; set; }
    public required List<AccessPointModel> AccessPoints { get; set; } = [];

    public virtual ProjectModel? Project { get; set; }
    public virtual ServerFarmModel? ServerFarm { get; set; }
    public virtual LocationModel? Location { get; set; }
    public virtual ICollection<SessionModel>? Sessions { get; set; }
    public virtual ICollection<ServerStatusModel>? ServerStatuses { get; set; }
}