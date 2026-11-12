using VpnHood.Net.Toolkit.Utils;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.App;
using VpnHood.Core.Client.Devices.Abstractions;

namespace VpnHood.AppUi.Hosting.Maui;

/// <summary>Provides the app under a MAUI UI.</summary>
/// <remarks>
/// The platform's own start runs the app, from the same init params as every head; a UI package never starts it.
/// </remarks>
public class VpnHoodAppMaui : Singleton<VpnHoodAppMaui>, IVpnHoodAppMaui
{
    private readonly IVpnHoodAppMaui _appMaui;

    private VpnHoodAppMaui(IVpnHoodAppMaui appMaui)
    {
        _appMaui = appMaui;
    }

    /// <summary>Gets the app's API, the contract every UI uses, in this process.</summary>
    /// <exception cref="InvalidOperationException">
    /// The app has not started or was disposed, as in an Android process other than the app's own.
    /// </exception>
    public VpnHoodApi Api => VpnHoodApp.Instance.Api;

    /// <summary>Starts the app under a MAUI UI, once per registered host: a disposed host lets another start.</summary>
    /// <param name="initParams">The head's init params.</param>
    /// <param name="device">
    /// On Windows, the head's device, which runs the VPN service in the app's one process, as a MAUI app there has no
    /// service; on Android, <see langword="null"/>, as Android starts the VPN service itself.
    /// </param>
    /// <returns>The app.</returns>
    /// <exception cref="OverflowException">A duration in a built-in access key is out of range.</exception>
    /// <exception cref="System.IO.InvalidDataException">A built-in access key decodes to JSON <c>null</c>.</exception>
    /// <exception cref="System.Text.Json.JsonException">A built-in access key holds no valid access key.</exception>
    /// <exception cref="NotSupportedException">A built-in access key's version is not one the app reads.</exception>
    /// <exception cref="FormatException">
    /// A built-in access key is no valid Base64, or an endpoint or a duration in it is not valid.
    /// </exception>
    /// <exception cref="VpnHood.Core.Common.Exceptions.AnotherInstanceIsRunningException">
    /// Another instance of the app holds the lock.
    /// </exception>
    /// <exception cref="ArgumentException">On Android, a device is given.</exception>
    /// <exception cref="ArgumentNullException">On Windows, no device is given.</exception>
    /// <exception cref="PlatformNotSupportedException">The platform is neither Android nor Windows.</exception>
    /// <exception cref="InvalidOperationException">
    /// The app has started already; the init params name no storage folder, or the platform has no local app data folder;
    /// or, on Android, the process's or the package's name cannot be read, Android names no cache folder or no folder kept
    /// out of backups, or, with the debug mode on, Android names no files folder or the storage folder is not directly in
    /// it.
    /// </exception>
    /// <remarks>
    /// On Android the app starts only in the app's own process. An ad provider that names countries both to include and to
    /// exclude fails the start.
    /// </remarks>
    public static VpnHoodAppMaui Init(AppInitParams initParams, IDevice? device = null)
    {
        return new VpnHoodAppMaui(CreateMauiApp(initParams, device));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// With <paramref name="disposing"/>, it disposes the app: on Android the VPN goes on, as Android runs the VPN service
    /// itself; on Windows the head's device stops it.
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _appMaui.Dispose();
        }

        base.Dispose(disposing);
    }

    internal static IVpnHoodAppMaui CreateMauiApp(AppInitParams initParams, IDevice? device = null)
    {
#if ANDROID
        return device is null
            ? VpnHoodAppMauiAndroid.Init(initParams)
            : throw new ArgumentException("Android starts the VPN service itself: a MAUI app there hands no device.",
                nameof(device));
#elif WINDOWS
        return VpnHoodAppMauiWindows.Init(initParams, device ??
            throw new ArgumentNullException(nameof(device),
                "A MAUI app on Windows runs the VPN service in its own process: hand its device (WindowsInProcessDevice)."));
#else
        throw new PlatformNotSupportedException();
#endif
    }
}
