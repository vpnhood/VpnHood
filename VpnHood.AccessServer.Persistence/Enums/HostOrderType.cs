using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Persistence.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HostOrderType
{
    NewIp = 1,
}