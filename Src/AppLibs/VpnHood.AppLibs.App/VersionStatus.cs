using System.Text.Json.Serialization;

namespace VpnHood.AppLibs;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VersionStatus
{
    Unknown,
    Latest,
    Old,
    Deprecated
}