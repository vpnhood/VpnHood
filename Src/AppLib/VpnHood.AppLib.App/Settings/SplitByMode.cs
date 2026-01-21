using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Settings;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SplitByMode
{
    All,
    Exclude,
    Include
}