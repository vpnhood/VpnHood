using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.Persistence.Models.HostOrders;

public class HostIpModel
{
    public int HostIpId { get; set; }
    public required Guid ProjectId { get; init; }

    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress IpAddress { get; init; }
    public required DateTime CreatedTime { get; init; }
    public required string ProviderName { get; set; }
    public required bool ExistsInProvider { get; set; }
    public required string? Description { get; set; }
    public DateTime? AutoReleaseTime { get; set; }
    public DateTime? ReleaseRequestTime { get; set; }
    public DateTime? DeletedTime { get; set; }

    public bool IsDeleted() => DeletedTime != null;
}