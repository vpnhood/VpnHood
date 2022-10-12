using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Models;

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
    [JsonIgnore]
    public byte[] Secret { get; set; } = default!;
    public Guid? AccessPointGroupId { get; set; } //AutoUpdateAccessPoint
    public Guid ConfigCode { get; set; }
    public Guid? LastConfigCode { get; set; }

    public virtual Project? Project { get; set; }
    public virtual AccessPointGroup? AccessPointGroup { get; set; }
    public virtual ServerStatusEx? ServerStatus { get; set; }

    [JsonIgnore] public virtual ICollection<AccessUsageEx>? AccessUsages { get; set; }
    [JsonIgnore] public virtual ICollection<Session>? Sessions { get; set; }
    [JsonIgnore] public virtual ICollection<AccessPoint>? AccessPoints { get; set; }
    [JsonIgnore] public virtual ICollection<ServerStatusEx>? ServerStatuses { get; set; }
}