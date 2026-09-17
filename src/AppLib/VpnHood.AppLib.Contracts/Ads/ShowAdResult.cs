using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Contracts.Ads;

[JsonConverter(typeof(JsonStringEnumConverter<ShowAdResult>))]
public enum ShowAdResult
{
    Closed,
    Clicked
}
