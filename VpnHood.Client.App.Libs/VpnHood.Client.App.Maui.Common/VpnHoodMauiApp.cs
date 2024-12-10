using VpnHood.Common.Utils;

namespace VpnHood.Client.App.Maui.Common;

public class VpnHoodMauiApp : Singleton<VpnHoodMauiApp>
{
    public static VpnHoodApp Init(AppOptions options)
    {
        var app = CreateApp();
        options.CultureProvider ??= app.CultureService;

        var vpnHoodApp =  VpnHoodApp.Init(app.Device, options);
        app.Init(vpnHoodApp);
        return vpnHoodApp;
    }

    private static IVpnHoodMauiApp CreateApp()
    {
#if ANDROID
        return new VpnHoodMauiAndroidApp();

#elif WINDOWS
        return new VpnHoodMauiWinUiApp();
#else
        throw new PlatformNotSupportedException();
#endif
    }
}
