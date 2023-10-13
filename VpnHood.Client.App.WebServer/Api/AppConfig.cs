using VpnHood.Client.App.Settings;

namespace VpnHood.Client.App.WebServer.Api;

public class AppConfig
{
    public required AppFeatures Features { get; init; }
    public required AppSettings Settings { get; init; }
    public required AppState AppState { get; init; }
    public required ClientProfileItem[] ClientProfileItems { get; init; }
}