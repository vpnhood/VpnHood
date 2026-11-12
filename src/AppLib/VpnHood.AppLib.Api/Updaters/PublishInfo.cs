namespace VpnHood.AppLib.Api.Updaters;

/// <summary>Represents a release as the app's update feed publishes it.</summary>
public class PublishInfo
{
    /// <summary>Gets the release's version.</summary>
    public required Version Version { get; init; }

    /// <summary>Gets the address of the feed that publishes it.</summary>
    public required Uri UpdateInfoUrl { get; init; }

    /// <summary>Gets the address of the release's package.</summary>
    public required Uri PackageUrl { get; init; }

    /// <summary>Gets the release's Google Play page, or <see langword="null"/>.</summary>
    public Uri? GooglePlayUrl { get; init; }

    /// <summary>Gets the page that tells how to install the release.</summary>
    public required Uri InstallationPageUrl { get; init; }

    /// <summary>Gets when the release was published, in UTC.</summary>
    public required DateTime ReleaseDate { get; init; }

    /// <summary>Gets the newest deprecated version: an app at it or older must update. The default is 0.0, none.</summary>
    public Version DeprecatedVersion { get; init; } = new();

    /// <summary>
    /// Gets how long after the release the app waits before it tells the person of it, which lets a store catch up.
    /// The default is none.
    /// </summary>
    public TimeSpan NotificationDelay { get; init; } = TimeSpan.Zero;
}