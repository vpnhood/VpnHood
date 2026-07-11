using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<AppProxyMode>))]
public enum AppProxyMode
{
    NoProxy,
    Device,
    Manual
}
