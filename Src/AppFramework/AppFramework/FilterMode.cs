using System.Text.Json.Serialization;

namespace VpnHood.AppFramework;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FilterMode
{
    All,
    Exclude,
    Include
}