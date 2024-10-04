﻿using VpnHood.AccessServer.Persistence.Enums;

namespace VpnHood.AccessServer.Persistence.Models.HostOrders;

public class HostOrderModel
{
    public required Guid ProjectId { get; init; }
    public required Guid HostOrderId { get; init; }
    public required DateTime CreatedTime { get; init; }
    public required HostOrderType OrderType { get; init; }
    public required Guid HostProviderId { get; init; }
    public required HostOrderStatus Status { get; set; }
    public required string? ErrorMessage { get; set; }
    public required string ProviderOrderId { get; init; }
    public required DateTime? CompletedTime { get; set; }
   
    public string? NewIpOrderIpAddress { get; set; }
    public Guid? NewIpOrderServerId { get; init; }
    public DateTime? NewIpOrderOldIpAddressReleaseTime { get; set; }

    public virtual ServerModel? NewIpOrderServer { get; set; }
    public virtual ProjectModel? Project { get; set; }
    public virtual HostProviderModel? HostProvider { get; set; }
}