using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Contracts.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<SplitAppMode>))]
public enum SplitAppMode
{
    All,
    Exclude,
    Include
}
