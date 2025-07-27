using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Tokens;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EndPointStrategy
{
    Auto = 0,
    DnsFirst = 1,
    TokenFirst = 2,
    DnsOnly = 3,
    TokenOnly = 4,
}