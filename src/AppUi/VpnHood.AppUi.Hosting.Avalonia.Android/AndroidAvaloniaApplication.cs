using Android.Runtime;
using Avalonia.Android;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Android;
// this file sits in a namespace called Avalonia, so the framework type needs an explicit name
using AvaloniaApplication = Avalonia.Application;

namespace VpnHood.AppUi.Hosting.Avalonia.Android;

// The Avalonia UI's Application, as AndroidAvaloniaMainActivity is its activity: a head's
// Application derives from this, says how its app starts (CreateInitParams), and carries only what
// the OS reads - its [Application] attribute. Avalonia 12 starts from the process's Application, so
// this is Avalonia's own base with the app started before it: the platform first, then the app's
// API handed to the UI (AvaloniaUiHosting.InitAsync), then Avalonia. The OS makes it in every
// process of the package - the VPN service's and the Quick Settings tile's too - where the platform
// starts no app and the UI is given no API, so no view is made there (VpnHoodAvaloniaAppBase). A
// head that must do something before all of this - an analytics SDK that reports from every
// process - overrides OnCreate and calls this one after.
public abstract class AndroidAvaloniaApplication<TUi>(IntPtr javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<TUi>(javaReference, transfer)
    where TUi : AvaloniaApplication, IAvaloniaUi, new()
{
    // The head's init params, asked for only in the app's own process (VpnHoodAndroidApp.Init).
    protected abstract AppInitParams CreateInitParams();

    public override void OnCreate()
    {
        VpnHoodAndroidApp.Init(CreateInitParams);
        if (VpnHoodApp.IsInit)
            AvaloniaUiHosting.InitAsync(VpnHoodApp.Instance.Api, CancellationToken.None).GetAwaiter().GetResult();

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
