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

    // inProcessVpnServiceFactory: the VPN service a MAUI app on Windows runs in its own process
    // (WindowsVpnServiceFactory), which the head hands in, as only the head references the VPN service's side; null
    // on Android, whose OS starts the VPN service itself.
    public static VpnHoodAppMaui Init(AppInitParams initParams, IInProcessVpnServiceFactory? inProcessVpnServiceFactory)
    {
        return new VpnHoodAppMaui(CreateMauiApp(initParams, inProcessVpnServiceFactory));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _appMaui.Dispose();
        }

        base.Dispose(disposing);
    }

    public static IVpnHoodAppMaui CreateMauiApp(AppInitParams initParams,
        IInProcessVpnServiceFactory? inProcessVpnServiceFactory)
    {
#if ANDROID
        return VpnHoodAppMauiAndroid.Init(initParams);
#elif WINDOWS
        return VpnHoodAppMauiWindows.Init(initParams, inProcessVpnServiceFactory ??
            throw new ArgumentNullException(nameof(inProcessVpnServiceFactory),
                "A MAUI app on Windows runs the VPN service in its own process."));
#else
        throw new PlatformNotSupportedException();
#endif
    }
}
