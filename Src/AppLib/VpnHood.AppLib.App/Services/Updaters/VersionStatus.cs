using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Services.Updaters;

[JsonConverter(typeof(JsonStringEnumConverter<VersionStatus>))]
public enum VersionStatus
{
    Unknown,
    Latest,
    Old,
    Deprecated
}
