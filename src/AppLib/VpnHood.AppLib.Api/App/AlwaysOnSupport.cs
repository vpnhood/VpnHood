using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.App;

// Who keeps the VPN on at all times, the way AppProxyMode says Device for the system's: the device's
// own setting, which the app only opens (Android's, IIntentsApi.OpenAlwaysOnSettings), or the app
// itself by UserSettings.AlwaysOn (a desktop's service, which starts at boot).
[JsonConverter(typeof(JsonStringEnumConverter<AlwaysOnSupport>))]
public enum AlwaysOnSupport
{
    None,
    Device,
    App
}
