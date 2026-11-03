using VpnHood.Core.Client.Devices.Abstractions;

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

    // The Windows service the daemon runs as, whose processes the kill switch's block lets out; null in a run as the
    // person.
    public string? ServiceName { get; init; }

    // The VPN host the daemon starts at each connect, the VPN service's process (WindowsVpnHost); null in a run as
    // the person, where the VPN service runs in the app's process, made by InProcessVpnServiceFactory.
    public string? VpnHostPath { get; init; }

    // The VPN service in the app's process, where there is no VPN host: the head's, as only a head references the VPN
    // service's side (WindowsVpnServiceFactory).
    public IInProcessVpnServiceFactory? InProcessVpnServiceFactory { get; init; }
}
