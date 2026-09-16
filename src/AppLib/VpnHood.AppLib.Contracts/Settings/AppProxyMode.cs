using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Contracts.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<AppProxyMode>))]
public enum AppProxyMode
{
    NoProxy,
    Device,
    Manual
}
