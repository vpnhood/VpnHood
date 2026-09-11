using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Settings;

namespace VpnHood.AppLib.WebServer.Api;

public class AppData
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