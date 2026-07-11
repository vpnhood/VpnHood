using System.Text.Json.Serialization;

namespace VpnHood.Core.Client.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter<DnsSelection>))]
public enum DnsSelection
{
    UserDns,
    ServerDns,
    GoogleDns
}
