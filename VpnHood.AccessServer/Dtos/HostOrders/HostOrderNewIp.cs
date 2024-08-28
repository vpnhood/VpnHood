using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.Dtos.HostOrders;

public class HostOrderNewIp
{
    public Guid ServerId { get; init; }
    public DateTime? OldIpAddressReleaseTime { get; init; }

    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress? OldIpAddress { get; init; }
}