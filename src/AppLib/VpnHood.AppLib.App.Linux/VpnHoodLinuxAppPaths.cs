namespace VpnHood.AppLib.App.Linux;

// Where VpnHoodLinuxApp keeps things, which its host decides by how it runs the app - the service, or a debugger's
// run - rather than the head. Named, as paths of one type are easily passed in each other's place.
/// <summary>
/// Represents where <see cref="VpnHoodLinuxApp"/> keeps things, which its host decides by how it runs the app: the
/// service, or a debugger's run.
/// </summary>
public class VpnHoodLinuxAppPaths
{
    // The app's storage: the daemon's folder beside its versions, or a debugger's run's own.
    /// <summary>Gets the app's storage: the daemon's folder beside its versions, or a debugger's run's own.</summary>
    public required string StoragePath { get; init; }

    // Where the single-instance lock lives (InstanceLockFile): the service's folder under /run, which only root may
    // write, so nobody takes the lock first, or a debugger's run's own storage. Not an abstract socket name, which
    // anyone may take first.
    /// <summary>Gets where the single-instance lock lives: the service's folder under <c>/run</c>, which only root may write, or a debugger's run's own storage.</summary>
    public required string LockFolderPath { get; init; }

    // The install's way out of the kill switch's block (AppOptions.KillSwitchOffScriptPath), which the host knows;
    // null outside an install.
    /// <summary>Gets the install's script that lifts the kill switch's block, or <see langword="null"/> outside an install.</summary>
    public string? KillSwitchOffScriptPath { get; init; }

    // The VPN host the daemon starts at each connect, the VPN service's process (LinuxVpnHost): the service's and a
    // debugger's run's alike.
    /// <summary>Gets the VPN host the daemon starts at each connect: the VPN service's process.</summary>
    public required string VpnHostPath { get; init; }
}
