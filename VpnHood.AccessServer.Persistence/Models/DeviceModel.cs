namespace VpnHood.AccessServer.Persistence.Models;

public class DeviceModel
{
    public required Guid DeviceId { get; init; }
    public Guid ProjectId { get; set; }
    public Guid ClientId { get; set; }
    public string? ClientVersion { get; set; }
    public string? IpAddress { get; set; }
    public string? Country { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedTime { get; set; }
    public DateTime LastUsedTime { get; set; }
    public DateTime? LockedTime { get; set; }

    public virtual ProjectModel? Project { get; set; }
    public virtual ICollection<AccessModel>? Accesses { get; set; }
    public virtual ICollection<SessionModel>? Sessions { get; set; }
}