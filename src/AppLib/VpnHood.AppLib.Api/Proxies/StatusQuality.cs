using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Proxies;

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
