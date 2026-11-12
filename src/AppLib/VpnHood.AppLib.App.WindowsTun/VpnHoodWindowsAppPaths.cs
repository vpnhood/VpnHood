namespace VpnHood.AppLib.App.WindowsTun;

// Where VpnHoodWindowsApp keeps things, which its host decides by how it runs the app - the service, or a run as the
// person - rather than the head. Named, as paths of one type are easily passed in each other's place.
/// <summary>
/// Represents where <see cref="VpnHoodWindowsApp"/> keeps things, which its host decides by how it runs the app: the
/// service, or a run as the person.
/// </summary>
public class VpnHoodWindowsAppPaths
{
    // The app's storage: the service's folder under ProgramData, or a run as the person's own.
    /// <summary>Gets the app's storage: the service's folder under ProgramData, or a run as the person's own.</summary>
    public required string StoragePath { get; init; }

    // Where a run as the person keeps its single-instance lock, a folder of their own (InstanceLockFile); null for
    // the service, whose lock only administrators can reach.
    /// <summary>Gets where a run as the person keeps its single-instance lock, or <see langword="null"/> for the service, whose lock only administrators can reach.</summary>
    public string? LockFolderPath { get; init; }

    // The Windows service the daemon runs as, whose processes the kill switch's block lets out; null in a run as the
    // person.
    /// <summary>Gets the Windows service the daemon runs as, whose processes the kill switch's block lets out, or <see langword="null"/> in a run as the person.</summary>
    public string? ServiceName { get; init; }

    // The VPN host the daemon starts at each connect, the VPN service's process (WindowsVpnHost), the service's and a
    // run as the person's alike; null where the head hands the app a device of its own (VpnHoodWindowsApp.Init).
    /// <summary>Gets the VPN host the daemon starts at each connect, or <see langword="null"/> where the head gives the app a device of its own.</summary>
    public string? VpnHostPath { get; init; }
}
