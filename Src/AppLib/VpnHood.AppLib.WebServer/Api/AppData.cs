using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Settings;

namespace VpnHood.AppLib.WebServer.Api;

public class AppData
{
    public required AppFeatures Features { get; init; }
    public required AppSettings Settings { get; init; }
    public required AppState State { get; init; }
    public required ClientProfileInfo[] ClientProfileInfos { get; init; }
    public required UiCultureInfo[] AvailableCultureInfos { get; init; }
}