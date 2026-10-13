namespace VpnHood.AppLib.Abstractions.Updaters;

// The feed's release that is newer than the running version
public class AppRelease
{
    public required Version Version { get; init; }
    public required Uri PackageUrl { get; init; }
}
