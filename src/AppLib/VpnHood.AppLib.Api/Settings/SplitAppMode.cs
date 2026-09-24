using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<SplitAppMode>))]
public enum SplitAppMode
{
    All,
    Exclude,
    Include
}
