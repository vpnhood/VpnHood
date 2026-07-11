using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<SplitCountryMode>))]
public enum SplitCountryMode
{
    IncludeAll,
    ExcludeMyCountry,
    ExcludeList,
    IncludeList
}
