using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Maui.Common;

public class VpnHoodAppMaui : Singleton<VpnHoodAppMaui>, IVpnHoodAppMaui
{
    private IVpnHoodAppMaui _appMaui;

    private VpnHoodAppMaui(IVpnHoodAppMaui appMaui)
    {
        _appMaui = appMaui;
    }

    public static VpnHoodAppMaui Init(AppOptions appOptions)
    {
        return new VpnHoodAppMaui(CreateMauiApp(appOptions));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _appMaui.Dispose();
        }

        base.Dispose(disposing);
    }

    public static IVpnHoodAppMaui CreateMauiApp(AppOptions appOptions)
    {
#if ANDROID
        return VpnHoodAppMauiAndroid.Init(appOptions);
#elif WINDOWS
        return VpnHoodAppMauiWin.Init(appOptions);
#else
        throw new PlatformNotSupportedException();
#endif
    }
}
