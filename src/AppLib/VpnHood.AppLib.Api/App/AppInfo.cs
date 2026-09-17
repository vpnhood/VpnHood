using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.ClientProfiles;
using VpnHood.AppLib.Api.Settings;

namespace VpnHood.AppLib.Api.App;

// Everything a UI needs to draw itself, in one read: what this build can do, what the app is
// doing now, what the person chose, and which profiles and languages exist. Configure returns it
// once the UI has declared its languages, GetInfo re-reads it whenever the state says the
// configuration moved (AppState.ConfigTime). Not named for configuration: it carries State,
// which moves every second.
public class AppInfo
{
    public required AppFeatures Features { get; init; }
    public required DeviceIntentFeatures IntentFeatures { get; init; }
    public required AppState State { get; init; }
    public required UserSettings UserSettings { get; init; }
    public required ClientProfileInfo[] ClientProfileInfos { get; init; }
    public required UiCultureInfo[] AvailableCultureInfos { get; init; }

    // Whether this client is another device on the LAN rather than the app's own web view. Set by
    // the route, not the controller: only the request knows where it came from. The SPA renders the
    // TV layout only for the TV itself, and hides the pairing entry from a phone.
    public bool IsRemote { get; set; }
}