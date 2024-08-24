using System.Net;
using System.Text.Json.Serialization;
using VpnHood.AccessServer.Dtos.Servers;
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
    public required string ProviderId { get; init; }
    public required string ProviderName { get; init; }
    public required string? ErrorMessage { get; set; }
    public required DateTime? CompletedTime { get; set; }
    
    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress? NewIpOrderIpAddress { get; set; }
    public required VpnServer? NewIpOrderServer { get; set; }
    
}