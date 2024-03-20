namespace VpnHood.AccessServer.Persistence.Models;

public class IpLockModel
{
    public Guid ProjectId { get; set; }
    public string IpAddress { get; set; } = default!;
    public DateTime? LockedTime { get; set; }
    public string? Description { get; set; }
}