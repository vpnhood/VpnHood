namespace VpnHood.AccessServer.Dtos;

public class Server
{
    public Guid ServerId { get; set; }
    public Guid? AccessPointGroupId { get; set; }
    public string? AccessPointGroupName { get; set; }
    public string? Version { get; set; }
    public string? ServerName { get; set; }
    public string? EnvironmentVersion { get; set; }
    public string? OsInfo { get; set; }
    public string? MachineName { get; set; }
    public long? TotalMemory { get; set; }
    public int? LogicalCoreCount { get; set; }
    public DateTime? ConfigureTime { get; set; }
    public DateTime CreatedTime { get; set; }
    public bool IsEnabled { get; set; }
    public string? Description { get; set; }
    public string? LastConfigError { get; set; }
    public ServerState ServerState { get; set; }
    public ServerStatusEx? ServerStatus { get; set; }
}