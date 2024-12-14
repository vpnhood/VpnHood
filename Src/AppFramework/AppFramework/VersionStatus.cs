using System.Text.Json.Serialization;

namespace VpnHood.AppFramework;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VersionStatus
{
    Unknown,
    Latest,
    Old,
    Deprecated
}