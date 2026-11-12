namespace VpnHood.AppLib.Abstractions.Updaters;

/// <summary>Represents the release in the app's update feed that is newer than the running version.</summary>
public class AppRelease
{
    /// <summary>Gets the release's version.</summary>
    public required Version Version { get; init; }

    /// <summary>Gets the address of the release's package.</summary>
    public required Uri PackageUrl { get; init; }
}
