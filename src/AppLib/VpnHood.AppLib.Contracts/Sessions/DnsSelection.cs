using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Contracts.Sessions;

[JsonConverter(typeof(JsonStringEnumConverter<DnsSelection>))]
public enum DnsSelection
{
    UserDns,
    ServerDns,
    GoogleDns
}
