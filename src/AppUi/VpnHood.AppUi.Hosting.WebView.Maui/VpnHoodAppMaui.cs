using VpnHood.Net.Toolkit.Utils;
using VpnHood.AppLib.App;

namespace VpnHood.AppUi.Hosting.WebView.Maui;

// The app under a MAUI UI. The app itself is started by the platform's own Init, from the same
// init params as every head (VpnHoodAndroidApp, VpnHoodWindowsApp): a UI package never starts it.
public class VpnHoodAppMaui : Singleton<VpnHoodAppMaui>, IVpnHoodAppMaui
{
    private readonly IVpnHoodAppMaui _appMaui;

    private VpnHoodAppMaui(IVpnHoodAppMaui appMaui)
    {
        _appMaui = appMaui;
    }

    public static VpnHoodAppMaui Init(AppInitParams initParams)
    {
        return new VpnHoodAppMaui(CreateMauiApp(initParams));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _appMaui.Dispose();
        }

        base.Dispose(disposing);
    }

    public static IVpnHoodAppMaui CreateMauiApp(AppInitParams initParams)
    {
#if ANDROID
        return VpnHoodAppMauiAndroid.Init(initParams);
#elif WINDOWS
        return VpnHoodAppMauiWindows.Init(initParams);
#else
        throw new PlatformNotSupportedException();
#endif
    }
}
