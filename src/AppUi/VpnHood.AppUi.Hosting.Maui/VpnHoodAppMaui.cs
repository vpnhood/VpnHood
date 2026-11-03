using VpnHood.Net.Toolkit.Utils;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.App;
using VpnHood.Core.Client.Devices.Abstractions;

namespace VpnHood.AppUi.Hosting.Maui;

// The app under a MAUI UI. The app itself is started by the platform's own Init, from the same
// init params as every head (VpnHoodAndroidApp, VpnHoodWindowsApp): a UI package never starts it.
public class VpnHoodAppMaui : Singleton<VpnHoodAppMaui>, IVpnHoodAppMaui
{
    private readonly IVpnHoodAppMaui _appMaui;

    private VpnHoodAppMaui(IVpnHoodAppMaui appMaui)
    {
        _appMaui = appMaui;
    }

    // What the MAUI pages talk to: the app's API, the contract every UI uses, here in this process.
    public VpnHoodApi Api => VpnHoodApp.Instance.Api;

    // device: on Windows, where a MAUI app has no daemon, the head runs the VPN service in its one process and hands
    // its device (VpnServices.Host.WindowsTun's WindowsInProcessDevice, on initParams.ResolveStoragePath()), as only a
    // head references the VPN service's side; none on Android, whose OS starts the VPN service itself.
    public static VpnHoodAppMaui Init(AppInitParams initParams, IDevice? device = null)
    {
        return new VpnHoodAppMaui(CreateMauiApp(initParams, device));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _appMaui.Dispose();
        }

        base.Dispose(disposing);
    }

    public static IVpnHoodAppMaui CreateMauiApp(AppInitParams initParams, IDevice? device = null)
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
