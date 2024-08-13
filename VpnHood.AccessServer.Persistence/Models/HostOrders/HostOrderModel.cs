using System.Net;
using System.Text.Json.Serialization;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.Persistence.Models.HostOrders;

public class HostOrderModel
{
    public required Guid ProjectId { get; init; }
    public required Guid HostOrderId { get; init; }
    public required DateTime CreatedTime { get; init; }
    public required HostOrderType OrderType { get; init; }
    public required HostOrderStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ProviderOrderId { get; init; }
    public DateTime? CompletedTime { get; set; }
    public Guid? NewIpOrderServerId { get; init; }
    
    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress? NewIpOrderIpAddress { get; set; }

    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress? ReleaseOrderIpAddress { get; init; }

    public virtual ServerModel? NewIpOrderServer { get; set; }
}