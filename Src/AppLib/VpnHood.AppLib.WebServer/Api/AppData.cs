using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Settings;

namespace VpnHood.AppLib.WebServer.Api;

public class AppData
{
    public required AppFeatures Features { get; init; }
    public required AppIntentFeatures IntentFeatures { get; init; }
    public required AppState State { get; init; }
    public required UserSettings UserSettings { get; init; }
    public required ClientProfileInfo[] ClientProfileInfos { get; init; }
    public required UiCultureInfo[] AvailableCultureInfos { get; init; }
}