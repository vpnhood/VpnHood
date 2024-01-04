namespace VpnHood.Client.App;

public class PublishInfo
{
    public required Version Version { get; init;}
    public required Uri UpdateInfoUrl { get; init; }
    public required Uri PackageUrl { get; init; }
    public required Uri? GooglePlayUrl { get; init; }
    public required Uri InstallationPageUrl { get; init; }
    public required DateTime ReleaseDate { get; init; }
    public required Version DeprecatedVersion { get; init; }
    public required TimeSpan NotificationDelay { get; init; }
}