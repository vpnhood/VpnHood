using System.Net;
using System.Text.Json.Serialization;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.Dtos.HostOrders;

public class HostOrder
{
    public required string OrderId { get; init; }
    public required DateTime CreatedTime { get; init; }
    public required HostOrderType OrderType { get; init; }
    public required HostOrderStatus Status { get; init; }
    public required string ProviderOrderId { get; init; }
    public required string HostProviderId { get; init; }
    public required string HostProviderName { get; init; }
    public required string? ErrorMessage { get; set; }
    public required DateTime? CompletedTime { get; set; }

    // Order Details
    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress? NewIpOrderIpAddress { get; set; }
    public required Guid? ServerId { get; init; }
    public required string? ServerName { get; set; }
    public required Location? ServerLocation { get; set; }
    public required Guid? ServerFarmId { get; set; }
    public required string? ServerFarmName { get; set; }

}