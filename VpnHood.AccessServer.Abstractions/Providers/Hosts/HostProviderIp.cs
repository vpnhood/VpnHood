using System.Net;

namespace VpnHood.AccessServer.Abstractions.Providers.Hosts;

public class HostProviderIp
{
    public required IPAddress IpAddress { get; init; }
    public required string? ServerId { get; init; }
    public required string? Description { get; init; }
}