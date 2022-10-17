namespace VpnHood.AccessServer.Dtos;

public class Server
{
    public Guid ProjectId { get; set; }
    public Guid ServerId { get; set; }
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
    public Guid AuthorizationCode { get; set; }
    public Guid? AccessPointGroupId { get; set; } 
    public Guid ConfigCode { get; set; }
    public Guid? LastConfigCode { get; set; }
    public Models.Project Project { get; set; } = null!;
    public Models.AccessPointGroup AccessPointGroup { get; set; } = null!;
    public Models.ServerStatusEx ServerStatus { get; set; } = null!;

    public static Server FromModel(Models.Server model)
    {
        return new Server
        {
            AccessPointGroup = model.AccessPointGroup ?? throw new ArgumentException($"{nameof(model.AccessPointGroup)} can not be null.", nameof(model)),
            AccessPointGroupId = model.AccessPointGroupId,
            AuthorizationCode = model.AuthorizationCode,
            ConfigCode = model.ConfigCode,
            ConfigureTime = model.ConfigureTime,
            CreatedTime = model.CreatedTime,
            Description = model.Description,
            EnvironmentVersion = model.EnvironmentVersion,
            IsEnabled = model.IsEnabled,
            LastConfigCode = model.LastConfigCode,
            LogClientIp = model.LogClientIp,
            LogLocalPort = model.LogLocalPort,
            MachineName = model.MachineName,
            OsInfo = model.OsInfo,
            Project = model.Project ?? throw new ArgumentException($"{nameof(model.Project)} can not be null.", nameof(model)), 
            ProjectId = model.ProjectId,
            ServerId = model.ServerId,
            ServerStatus = model.ServerStatus ?? throw new ArgumentException($"{nameof(model.ServerStatus)} can not be null.", nameof(model)),
            ServerName = model.ServerName,
            TotalMemory = model.TotalMemory,
            Version = model.Version
        };
    }
}