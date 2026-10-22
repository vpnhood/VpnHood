using System.Runtime.InteropServices;
using Windows.Storage;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin;

// The package's LocalState, which the app and its VPN host share: the one folder both reach, the VPN host
// running in an AppContainer. Without package identity there is no LocalState, no profile and no plug-in.
public static class WindowsVpnPluginFolders
{
    private const int AppModelErrorNoPackage = 15700;

    // LocalState itself: the app's storage, which holds the VPN service's folder as a WindowsDevice's does
    public static string LocalFolder {
        get {
            if (!IsPackaged)
                throw new InvalidOperationException(
                    "The Windows VPN plug-in runs only from its MSIX package: install the package and start it from there.");

            return ApplicationData.Current.LocalFolder.Path;
        }
    }

    // the VPN service's files - its options, status and log - and the proxies, together: nothing backs up
    // a desktop app's data
    public static string VpnServiceFolder => Path.Combine(LocalFolder, "vpn-service");

    public static bool IsPackaged {
        get {
            var length = 0;
            return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
}
