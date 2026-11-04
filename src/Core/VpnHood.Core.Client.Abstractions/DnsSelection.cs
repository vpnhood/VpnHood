using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Client.Abstractions;

// A name this side does not know reads as UserDns: it only says which servers answered.
[JsonConverter(typeof(JsonStringEnumFallbackConverter<DnsSelection>))]
[JsonEnumFallback(DnsSelection.UserDns)]
public enum DnsSelection
{
    UserDns,
    ServerDns,
    GoogleDns
}
