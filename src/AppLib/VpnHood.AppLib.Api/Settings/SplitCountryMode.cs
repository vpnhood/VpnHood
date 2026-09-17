using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<SplitCountryMode>))]
public enum SplitCountryMode
{
    IncludeAll,
    ExcludeMyCountry,
    ExcludeList,
    IncludeList
}
