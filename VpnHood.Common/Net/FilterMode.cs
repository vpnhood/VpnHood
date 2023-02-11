using System.Text.Json.Serialization;

namespace VpnHood.Common.Net;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FilterMode
{
    All,
    Exclude,
    Include
}