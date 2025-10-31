using System.Text.Json.Serialization;

namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter))]
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