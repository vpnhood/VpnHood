using System.Text.Json.Serialization;

namespace VpnHood.AppLibs;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FilterMode
{
    All,
    Exclude,
    Include
}