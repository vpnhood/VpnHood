using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.AppLib.Api.Settings;

namespace VpnHood.AppLib.Api.App;

/// <summary>
/// Represents everything a UI needs to draw itself, in one read: what this build can do, what the app is doing now, what
/// the person chose, and which VPN profiles and languages exist.
/// </summary>
/// <remarks>
/// <see cref="IAppApi.Configure"/> gives it once the UI has declared its languages, and <see cref="IAppApi.GetInfo"/>
/// reads it again whenever the state says the settings changed (<see cref="AppState.ConfigTime"/>).
/// </remarks>
public class AppInfo
{
    /// <summary>Gets what the app is and offers on this device.</summary>
    public required AppDescriptor Descriptor { get; init; }

    /// <summary>Gets what the UI may ask of the device.</summary>
    public required DeviceIntentFeatures IntentFeatures { get; init; }

    /// <summary>Gets the app's state as it stands.</summary>
    public required AppState State { get; init; }

    /// <summary>Gets the person's settings.</summary>
    public required UserSettings UserSettings { get; init; }

    /// <summary>Gets the VPN profiles.</summary>
    public required IReadOnlyList<VpnProfileInfo> VpnProfileInfos { get; init; }

    /// <summary>Gets the languages the UI offers.</summary>
    public required IReadOnlyList<UiCultureInfo> AvailableCultureInfos { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether this client is another device on the local network rather than the app
    /// itself; the web host sets it, as only the request knows where it came from.
    /// </summary>
    /// <remarks>The UI draws the TV layout only for the TV itself, and hides the pairing from a phone.</remarks>
    public bool IsRemote { get; set; }
}