using VpnHood.AppLib.App.Android;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.AppLib.App;

// ReSharper disable once CheckNamespace
namespace VpnHood.AppUi.Hosting.WebView.Maui;

internal class VpnHoodAppMauiAndroid : Singleton<VpnHoodAppMauiAndroid>, IVpnHoodAppMaui
{
    // The platform starts the app - only in the app's own process, with its own defaults - as it
    // does under any other UI.
    public static VpnHoodAppMauiAndroid Init(AppInitParams initParams)
    {
        VpnHoodAndroidApp.Init(() => initParams);
        return new VpnHoodAppMauiAndroid();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            if (VpnHoodAndroidApp.IsInit)
                VpnHoodAndroidApp.Instance.Dispose();
        }

        base.Dispose(disposing);
    }
}
