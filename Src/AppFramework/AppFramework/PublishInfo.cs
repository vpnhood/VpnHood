namespace VpnHood.AppFramework;

public class PublishInfo
{
    public required Version Version { get; init; }
    public required Uri UpdateInfoUrl { get; init; }
    public required Uri PackageUrl { get; init; }
    public Uri? GooglePlayUrl { get; init; }
    public required Uri InstallationPageUrl { get; init; }
    public required DateTime ReleaseDate { get; init; }
    public Version DeprecatedVersion { get; init; } = new(0, 0, 0, 0);
    public TimeSpan NotificationDelay { get; init; } = TimeSpan.Zero;
}