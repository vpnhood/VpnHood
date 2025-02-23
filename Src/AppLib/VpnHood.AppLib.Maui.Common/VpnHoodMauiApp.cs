using VpnHood.Core.Common.Utils;

namespace VpnHood.AppLib.Maui.Common;

public class VpnHoodMauiApp : Singleton<VpnHoodMauiApp>
{
    public static VpnHoodApp Init(AppOptions options)
    {
        var app = CreateApp();
        return app.Init(options);
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
