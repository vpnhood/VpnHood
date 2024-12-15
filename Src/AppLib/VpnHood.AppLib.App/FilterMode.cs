using System.Text.Json.Serialization;

namespace VpnHood.AppLib;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FilterMode
{
    All,
    Exclude,
    Include
}