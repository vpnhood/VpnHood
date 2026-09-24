using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Sessions;

[JsonConverter(typeof(JsonStringEnumConverter<DnsSelection>))]
public enum DnsSelection
{
    UserDns,
    ServerDns,
    GoogleDns
}
