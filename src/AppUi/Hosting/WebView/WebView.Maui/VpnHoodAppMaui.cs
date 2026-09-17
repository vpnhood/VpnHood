using VpnHood.Core.Toolkit.Utils;
using VpnHood.AppLib;

namespace VpnHood.AppUi.Hosting.WebView.Maui;

public class VpnHoodAppMaui : Singleton<VpnHoodAppMaui>, IVpnHoodAppMaui
{
    private readonly IVpnHoodAppMaui _appMaui;

    private VpnHoodAppMaui(IVpnHoodAppMaui appMaui)
    {
        _appMaui = appMaui;
    }

    public static VpnHoodAppMaui Init(Func<AppOptions> optionsFactory)
    {
        return new VpnHoodAppMaui(CreateMauiApp(optionsFactory));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _appMaui.Dispose();
        }

        base.Dispose(disposing);
    }

    public static IVpnHoodAppMaui CreateMauiApp(Func<AppOptions> optionsFactory)
    {
#if ANDROID
        return VpnHoodAppMauiAndroid.Init(optionsFactory);
#elif WINDOWS
        return VpnHoodAppMauiWin.Init(optionsFactory);
#else
        throw new PlatformNotSupportedException();
#endif
    }
}
