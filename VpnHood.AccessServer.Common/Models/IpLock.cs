namespace VpnHood.AccessServer.Models;

public class IpLock
{
    public Guid ProjectId { get; set; }
    public string IpAddress { get; set; } = default!;
    public DateTime? LockedTime { get; set; }
    public string? Description { get; set; }
}