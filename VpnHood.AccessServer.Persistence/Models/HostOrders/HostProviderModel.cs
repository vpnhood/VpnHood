
namespace VpnHood.AccessServer.Persistence.Models.HostOrders;

public class HostProviderModel
{
    public required Guid HostProviderId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string HostProviderName { get; init; }
    public required string Settings { get; set; }
    public required string? CustomData { get; set; }

    public virtual ProjectModel? Project { get; set; }
    public virtual ICollection<HostOrderModel>? HostOrders { get; set; }
    public virtual ICollection<HostIpModel>? HostIps { get; set; }
}