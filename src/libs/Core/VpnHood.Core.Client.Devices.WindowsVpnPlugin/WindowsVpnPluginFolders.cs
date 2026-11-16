using System.Runtime.InteropServices;
using Windows.Storage;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin;

/// <summary>
/// Provides the package's LocalState, which the app and its VPN host share: the one folder both reach, the VPN host
/// running in an AppContainer.
/// </summary>
/// <remarks>Without package identity there is no LocalState, no profile and no plug-in.</remarks>
public static class WindowsVpnPluginFolders
{
    private const int AppModelErrorNoPackage = 15700;

    /// <summary>
    /// Gets LocalState itself: the app's storage, which holds the VPN service's folder as a Windows device's storage does.
    /// </summary>
    /// <exception cref="InvalidOperationException">The process does not run from the package.</exception>
    public static string LocalFolder {
        get {
            if (!IsPackaged)
                throw new InvalidOperationException(
                    "The Windows VPN plug-in runs only from its MSIX package: install the package and start it from there.");

            return ApplicationData.Current.LocalFolder.Path;
        }
    }

    /// <summary>
    /// Gets the folder of the VPN service's files — its options, status and log — and the proxies, together:
    /// <c>vpn-service</c> under <see cref="LocalFolder"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The process does not run from the package.</exception>
    public static string VpnServiceFolder => Path.Combine(LocalFolder, "vpn-service");

    private static bool IsPackaged {
        get {
            var length = 0;
            return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
}
