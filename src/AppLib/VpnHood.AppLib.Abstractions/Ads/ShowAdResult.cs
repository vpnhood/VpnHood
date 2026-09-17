using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Abstractions.Ads;

[JsonConverter(typeof(JsonStringEnumConverter<ShowAdResult>))]
public enum ShowAdResult
{
    Closed,
    Clicked
}
