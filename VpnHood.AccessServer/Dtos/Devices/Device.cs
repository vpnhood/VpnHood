using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Dtos.Devices;

public class Device
{
    public required Guid DeviceId { get; init; }
    public required string ClientId { get; init; }
    public required string? ClientVersion { get; init; }
    public required string? IpAddress { get; init; }
    public required Location? Location { get; init; }
    public required string? UserAgent { get; init; }
    public required DateTime CreatedTime { get; init; }
    public required DateTime ModifiedTime { get; init; }
    public required DateTime? LockedTime { get; init; }
    public string OsName => UserAgentParser.GetOperatingSystem(UserAgent);
}