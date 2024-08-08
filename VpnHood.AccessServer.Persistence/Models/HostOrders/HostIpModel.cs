using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.Persistence.Models.HostOrders;

public class HostIpModel
{
    public required Guid ProjectId { get; init; }
    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress IpAddress { get; init; }
    public required DateTime CreatedTime { get; init; }
    public DateTime? ReleasedTime { get; set; }
    public bool IsDeleted { get; init; }
    public virtual ICollection<HostOrderModel>? HostOrders { get; set; }

}