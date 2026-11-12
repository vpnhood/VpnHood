using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.App;

/// <summary>Specifies who keeps the VPN on at all times on this device.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AlwaysOnSupport>))]
public enum AlwaysOnSupport
{
    /// <summary>Nobody: the device has no always-on VPN.</summary>
    None,

    /// <summary>The device's own setting, which the app only opens (<see cref="IIntentsApi.OpenAlwaysOnSettings"/>), as Android's.</summary>
    Device,

    /// <summary>The app itself, by the person's setting (<see cref="VpnHood.AppLib.Api.Settings.UserSettings.AlwaysOn"/>), as a desktop's service that starts at boot.</summary>
    App
}
