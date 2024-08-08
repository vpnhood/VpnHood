using System.Net;

namespace VpnHood.AccessServer.Abstractions.Providers.Hosts;

public class HostProviderIpOrder
{
    public required string OrderId { get; init; }
    public required IPAddress? IpAddress { get; init; }
    public required string? Description { get; init; }
    public required HostProviderOrderStatus Status { get; init; }
    public required string? Error { get; init; }
}