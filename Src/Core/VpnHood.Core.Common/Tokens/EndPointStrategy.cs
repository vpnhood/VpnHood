using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Tokens;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EndPointStrategy
{
    Auto = 0,
    DnsFirst = 1,
    IpFirst = 2,
    DnsOnly = 3,
    IpOnly = 4,

    // obsolete values
    [Obsolete]
    TokenFirst = 2,
    [Obsolete]
    TokenOnly = 4
}