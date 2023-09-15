using Android.App;
using Android.Runtime;

namespace VpnHood.Client.App.Maui;

[Application(Debuggable = true, UsesCleartextTraffic = true)]
public class MainApplication : MauiApplication
{
    private AppNotification? _notification;

    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp()
    {
        var mauiApp = MauiProgram.CreateMauiApp();
        _notification = new AppNotification(this);
        VpnHoodApp.Instance.ConnectionStateChanged += (_, _) => _notification.UpdateNotification();
        return mauiApp;
    }
}