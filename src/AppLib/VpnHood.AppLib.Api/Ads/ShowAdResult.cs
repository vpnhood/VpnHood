using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Ads;

[JsonConverter(typeof(JsonStringEnumConverter<ShowAdResult>))]
public enum ShowAdResult
{
    Closed,
    Clicked
}
