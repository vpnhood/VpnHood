using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Updaters;

[JsonConverter(typeof(JsonStringEnumConverter<AppUpdateStage>))]
public enum AppUpdateStage
{
    None,
    Available,
    Downloading,
    Downloaded,
    Installing,
    Failed
}
