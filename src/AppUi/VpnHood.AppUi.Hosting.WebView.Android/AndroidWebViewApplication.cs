using Android.Runtime;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Android;

namespace VpnHood.AppUi.Hosting.WebView.Android;

// The web view UI's Application, as AndroidWebViewMainActivity is its activity: a head's
// Application derives from this, says how its app starts (CreateInitParams), and carries only what
// the OS reads - its [Application] attribute. The OS makes it in every process of the package - the
// VPN service's and the Quick Settings tile's too - and the platform starts the app only in the
// app's own; the page itself is the activity's (WebViewHost). A head that must do something before
// the app starts - an analytics SDK that reports from every process - overrides OnCreate and calls
// this one after.
public abstract class AndroidWebViewApplication(IntPtr javaReference, JniHandleOwnership transfer)
    : Application(javaReference, transfer)
{
    // The head's init params, asked for only in the app's own process (VpnHoodAndroidApp.Init).
    protected abstract AppInitParams CreateInitParams();

    public override void OnCreate()
    {
        VpnHoodAndroidApp.Init(CreateInitParams);
        base.OnCreate();
    }

    // Called only on an emulator; a device ends the process without it.
    public override void OnTerminate()
    {
        if (VpnHoodAndroidApp.IsInit)
            VpnHoodAndroidApp.Instance.Dispose();

        base.OnTerminate();
    }
}
