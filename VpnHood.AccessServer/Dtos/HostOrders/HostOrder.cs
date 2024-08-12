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
    public string? ErrorMessage { get; set; }
    public DateTime? CompletedTime { get; set; }
    public required string ProviderOrderId { get; init; }
    
    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress? NewIpOrderIpAddress { get; set; }
    public VpnServer? NewIpOrderServer { get; set; }
}