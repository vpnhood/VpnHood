using VpnHood.AccessServer.Persistence.Enums;

namespace VpnHood.AccessServer.Persistence.Models.HostOrders;

public class HostOrderModel
{
    public required Guid ProjectId { get; init; }
    public required int OrderId { get; init; }
    public int? HostIpId { get; set; }
    public required DateTime CreatedTime { get; init; }
    public required HostOrderType OrderType { get; init; }
    public required HostOrderStatus Status { get; init; }
    public string? ErrorMessage { get; set; }
    public DateTime? CompletedTime { get; set; }
    public required string ProviderOrderId { get; init; }

    public virtual HostIpModel? HostIp { get; set; }

}