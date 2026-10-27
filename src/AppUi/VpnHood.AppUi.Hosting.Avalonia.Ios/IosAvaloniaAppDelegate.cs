using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.iOS;
using Avalonia.Platform.Storage;
using Foundation;
using UIKit;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Ios;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppUi.Hosting.Avalonia.Ios;

// The Avalonia UI's application delegate on iOS, as AndroidAvaloniaApplication is its Application
// on Android: a head's AppDelegate derives from this, says how its app starts (CreateInitParams),
// and carries only what the OS reads - its [Register] name. Avalonia's own delegate builds the UI as
// launching finishes and gives each scene its AvaloniaSceneDelegate, which is why a head's
// Info.plist names no scene delegate. The app is started at the one step of that launch a delegate
// is asked for, the app builder; then the UI is given the app's API - its own controllers in
// process, the same six interfaces a paired browser dials over HTTP - and its content, from the
// store's zip in the app bundle, where the build placed it (AvaloniaUiHosting).
public abstract class IosAvaloniaAppDelegate<TUi> : AvaloniaAppDelegate<TUi>
    where TUi : Application, IAvaloniaUi, new()
{
    private NSObject? _foregroundObserver; // kept for the app's life, as the observation is

    // A file or a link the app is opened with - an access key's, as the head's Info.plist declares - goes to the UI,
    // which imports it. Avalonia raises it as the scene connects, after the UI has made its view, and later as one
    // comes to the running app. A web page's link (a universal link) is not a key.
    protected IosAvaloniaAppDelegate()
    {
        ((IAvaloniaAppDelegate)this).Activated += OnActivated;
    }

    // The head's init params: the app's, and the two iOS facts the platform builds its device from.
    protected abstract IosInitParams CreateInitParams();

    // An iPhone stays upright: the UI's wide layout is for an iPad, not a phone on its side. iOS takes
    // this answer over the Info.plist's orientations; an iPad turns every way, and shares the
    // screen with other apps too (no UIRequiresFullScreen in the heads' Info.plist): nothing here
    // needs the whole screen.
    [Export("application:supportedInterfaceOrientationsForWindow:")]
    public UIInterfaceOrientationMask GetSupportedInterfaceOrientations(UIApplication application, UIWindow? forWindow)
    {
        return UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Phone
            ? UIInterfaceOrientationMask.Portrait
            : UIInterfaceOrientationMask.All;
    }

    private static void OnActivated(object? sender, ActivatedEventArgs e)
    {
        if (Application.Current is not VpnHoodAvaloniaAppBase ui)
            return;

        switch (e) {
            case FileActivatedEventArgs { Files: [IStorageFile file, ..] }:
                _ = ui.ImportAccessKeyFile(file);
                break;

            // the link as it came: Uri.ToString unescapes
            case ProtocolActivatedEventArgs { Uri: { Scheme: not ("http" or "https") } uri }:
                _ = ui.ImportAccessKey(uri.OriginalString);
                break;
        }
    }

    protected override AppBuilder CreateAppBuilder()
    {
        VpnHoodIosApp.Init(CreateInitParams());
        AvaloniaUiHosting.StartAsync<TUi>(VpnHoodApp.Instance.Api, VpnHoodApp.Instance.UiAssetProvider,
            CancellationToken.None).GetAwaiter().GetResult();
        AppUiContext.Context = new IosUiContext();
        _foregroundObserver = UIApplication.Notifications.ObserveWillEnterForeground(
            (_, _) => AppUiContext.NotifyResumed());
        return base.CreateAppBuilder();
    }
}
