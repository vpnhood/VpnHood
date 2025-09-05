using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShowAdResult
{
    Closed,
    Clicked
}
