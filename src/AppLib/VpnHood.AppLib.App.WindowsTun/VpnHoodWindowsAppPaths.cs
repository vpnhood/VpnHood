namespace VpnHood.AppLib.App.WindowsTun;

// Where VpnHoodWindowsApp keeps things, which its host decides by how it runs the app - the service, or a run as the
// person - rather than the head. Named, as paths of one type are easily passed in each other's place.
public class VpnHoodWindowsAppPaths
{
    // The app's storage: the service's folder under ProgramData, or a run as the person's own.
    public required string StoragePath { get; init; }

    // Where a run as the person keeps its single-instance lock, a folder of their own (InstanceLockFile); null for
    // the service, whose lock only administrators can reach.
    public string? LockFolderPath { get; init; }
}
