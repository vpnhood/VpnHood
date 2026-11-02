namespace VpnHood.AppLib.App.Linux;

// Where VpnHoodLinuxApp keeps things, which its host decides by how it runs the app - the service, or a debugger's
// run - rather than the head. Named, as paths of one type are easily passed in each other's place.
public class VpnHoodLinuxAppPaths
{
    // The app's storage: the daemon's folder beside its versions, or a debugger's run's own.
    public required string StoragePath { get; init; }

    // Where the single-instance lock lives (InstanceLockFile): the service's folder under /run, which only root may
    // write, so nobody takes the lock first, or a debugger's run's own storage. Not an abstract socket name, which
    // anyone may take first.
    public required string LockFolderPath { get; init; }

    // The install's way out of the kill switch's block (AppOptions.KillSwitchOffScriptPath), which the host knows;
    // null outside an install.
    public string? KillSwitchOffScriptPath { get; init; }
}
