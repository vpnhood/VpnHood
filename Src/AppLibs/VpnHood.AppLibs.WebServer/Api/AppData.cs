using VpnHood.AppLibs.ClientProfiles;
using VpnHood.AppLibs.Settings;

namespace VpnHood.AppLibs.WebServer.Api;

public class AppData
{
    public required AppFeatures Features { get; init; }
    public required AppSettings Settings { get; init; }
    public required AppState State { get; init; }
    public required ClientProfileInfo[] ClientProfileInfos { get; init; }
    public required UiCultureInfo[] AvailableCultureInfos { get; init; }
}