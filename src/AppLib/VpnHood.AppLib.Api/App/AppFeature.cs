using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.App;

/// <summary>Specifies a feature of the app, which its premium tier may keep for premium.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AppFeature>))]
public enum AppFeature
{
    /// <summary>The Quick Launch, a shortcut in the system's own UI that connects the VPN.</summary>
    QuickLaunch,

    /// <summary>The always-on VPN.</summary>
    AlwaysOn,

    /// <summary>The kill switch.</summary>
    KillSwitch,

    /// <summary>The person's own DNS servers.</summary>
    CustomDns,

    /// <summary>The split tunneling by IP ranges in the app.</summary>
    SplitIpViaApp,

    /// <summary>The split tunneling by IP ranges in the device's VPN adapter.</summary>
    SplitIpViaDevice,

    /// <summary>The split tunneling by domains.</summary>
    SplitDomain,

    /// <summary>The split tunneling by country.</summary>
    SplitCountry
}
