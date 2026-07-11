using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter<ShowAdResult>))]
public enum ShowAdResult
{
    Closed,
    Clicked
}
