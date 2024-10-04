using System.Net;

namespace VpnHood.AccessServer.Persistence.Models.HostOrders;

public class HostIpModel
{
    public int HostIpId { get; set; }
    public required Guid ProjectId { get; init; }
    public required string IpAddress { get; init; }
    public required DateTime CreatedTime { get; init; }
    public required Guid HostProviderId { get; set; }
    public required bool ExistsInProvider { get; set; }
    public required string? Description { get; set; }
    public Guid? RenewOrderId { get; set; }
    public DateTime? AutoReleaseTime { get; set; }
    public DateTime? ReleaseRequestTime { get; set; }
    public DateTime? DeletedTime { get; set; }

    public IPAddress GetIpAddress() => IPAddress.Parse(IpAddress);
    public bool IsDeleted() => DeletedTime != null;
    public virtual ProjectModel? Project { get; set; }
    public virtual HostProviderModel? HostProvider { get; set; }
}