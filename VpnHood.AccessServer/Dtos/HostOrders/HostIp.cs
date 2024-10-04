using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.Dtos.HostOrders;

public class HostIp
{
    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress IpAddress { get; init; }
    public required string ProviderId { get; init; }
    public required string ProviderName { get; init; }
    public required DateTime CreatedTime { get; init; }
    public required DateTime? AutoReleaseTime { get; init; }
    public required DateTime? ReleaseRequestTime { get; init; }
    public required Guid? ServerId { get; init; }
    public required bool ExistsInProvider { get; init; }
    public required string? ProviderDescription { get; init; }
    public required string? ServerName { get; set; }
    public required Location? ServerLocation { get; set; }
    public required Guid? ServerFarmId { get; set; }
    public required string? ServerFarmName { get; set; }
    public required HostIpStatus Status { get; set; }
}