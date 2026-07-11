using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<SplitAppMode>))]
public enum SplitAppMode
{
    All,
    Exclude,
    Include
}
