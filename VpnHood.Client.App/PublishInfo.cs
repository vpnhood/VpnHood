using System;

namespace VpnHood.Client.App;

public class PublishInfo
{
    public PublishInfo(Version version, Uri updateInfoUrl, Uri packageUrl, Uri installationPageUrl,
        Version deprecatedVersion, DateTime releaseDate, TimeSpan notificationDelay)
    {
        Version = version;
        UpdateInfoUrl = updateInfoUrl;
        PackageUrl = packageUrl;
        InstallationPageUrl = installationPageUrl;
        DeprecatedVersion = deprecatedVersion;
        ReleaseDate = releaseDate;
        NotificationDelay = notificationDelay;
    }

    public Version Version { get; }
    public Uri UpdateInfoUrl { get; }
    public Uri PackageUrl { get; }
    public Uri InstallationPageUrl { get; }
    public DateTime ReleaseDate { get; }
    public Version DeprecatedVersion { get; }
    public TimeSpan NotificationDelay { get; }
}