using VpnHood.AccessServer.ServerUtils;

namespace VpnHood.AccessServer.Dtos;

public class Server
{
    public Guid ServerId { get; set; }
    public Guid? AccessPointGroupId { get; set; }
    public bool LogClientIp { get; set; }
    public bool LogLocalPort { get; set; }
    public string? Version { get; set; }
    public string? ServerName { get; set; }
    public string? EnvironmentVersion { get; set; }
    public string? OsInfo { get; set; }
    public string? MachineName { get; set; }
    public long TotalMemory { get; set; }
    public DateTime? ConfigureTime { get; set; }
    public DateTime CreatedTime { get; set; }
    public bool IsEnabled { get; set; }
    public string? Description { get; set; }
    public string? LastConfigError { get; set; }
    public ServerState ServerState { get; set; }
    public Models.AccessPointGroupModel? AccessPointGroup { get; set; } 
    public ServerStatusEx? ServerStatus { get; set; } 
}