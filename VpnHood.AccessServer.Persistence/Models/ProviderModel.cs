using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models.HostOrders;

namespace VpnHood.AccessServer.Persistence.Models;

public class ProviderModel
{
    public required Guid ProviderId { get; init; }
    public required Guid ProjectId { get; init; }
    public required ProviderType ProviderType { get; init; }
    public required string ProviderName { get; init; }
    public required string Settings { get; set; }

    public virtual ProjectModel? Project { get; set; }
    public virtual ICollection<HostOrderModel>? HostOrders { get; set; }
    public virtual ICollection<HostIpModel>? HostIps { get; set; }
}