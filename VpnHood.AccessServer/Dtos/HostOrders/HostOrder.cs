using System.Net;
using VpnHood.AccessServer.Persistence.Enums;

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
    public IPAddress? IpAddress { get; set; }
}