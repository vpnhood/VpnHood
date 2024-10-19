using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.Abstractions.Providers.Hosts;

public class HostProviderIp
{
    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress IpAddress { get; init; }
    public required string? ServerId { get; init; }
    public required string? Description { get; init; }
    public required bool IsAdditional { get; init; }
}