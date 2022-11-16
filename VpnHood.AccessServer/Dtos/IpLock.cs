using System;

namespace VpnHood.AccessServer.Dtos;

public class IpLock
{
    public string IpAddress { get; set; } = default!;
    public DateTime? LockedTime { get; set; }
    public string? Description { get; set; }
}