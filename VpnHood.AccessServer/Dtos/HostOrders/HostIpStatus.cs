using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Dtos.HostOrders;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HostIpStatus
{
    InUse,
    NotInUse,
    NotInProvider,
    Releasing
}