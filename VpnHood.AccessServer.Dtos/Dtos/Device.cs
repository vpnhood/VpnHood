using System;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Dtos;

public class Device
{
    public Guid DeviceId { get; set; }
    public Guid ClientId { get; set; }
    public string? ClientVersion { get; set; }
    public string? IpAddress { get; set; }
    public string? Country { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedTime { get; set; }
    public DateTime ModifiedTime { get; set; }
    public DateTime? LockedTime { get; set; }
    public string OsName => UserAgentParser.GetOperatingSystem(UserAgent);
}
