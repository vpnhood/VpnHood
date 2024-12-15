using System.Text.Json.Serialization;

namespace VpnHood.AppLib;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VersionStatus
{
    Unknown,
    Latest,
    Old,
    Deprecated
}