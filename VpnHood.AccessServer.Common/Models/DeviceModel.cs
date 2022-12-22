namespace VpnHood.AccessServer.Models;

public class DeviceModel
{
    public Guid DeviceId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ClientId { get; set; }
    public string? ClientVersion { get; set; }
    public string? IpAddress { get; set; }
    public string? Country { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedTime { get; set; }
    public DateTime ModifiedTime { get; set; }
    public DateTime? LockedTime { get; set; }

    public virtual ProjectModel? Project { get; set; }
    public virtual ICollection<AccessModel>? Accesses { get; set; }

    public DeviceModel(Guid deviceId)
    {
        DeviceId = deviceId;
    }
}
