using System.Text.Json.Serialization;

namespace VpnHood.Client.App;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VersionStatus
{
    Unknown,
    Latest,
    Old,
    Deprecated
}