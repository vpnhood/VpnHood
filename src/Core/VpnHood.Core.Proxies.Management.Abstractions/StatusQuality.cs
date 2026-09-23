using System.Text.Json.Serialization;

namespace VpnHood.Core.Proxies.Management.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter<StatusQuality>))]
public enum StatusQuality
{
    Unknown,
    Excellent,
    Good,
    Fair,
    Poor,
    VeryPoor,
    Failed
}
