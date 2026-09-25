using Avalonia;
using Avalonia.iOS;
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
    // The head's init params: the app's, and the two iOS facts the platform builds its device from.
    protected abstract IosInitParams CreateInitParams();

    protected override AppBuilder CreateAppBuilder()
    {
        VpnHoodIosApp.Init(CreateInitParams());
        AvaloniaUiHosting.StartAsync<TUi>(VpnHoodApp.Instance.Api, VpnHoodApp.Instance.UiAssetProvider,
            CancellationToken.None).GetAwaiter().GetResult();
        AppUiContext.Context = new IosUiContext();
        return base.CreateAppBuilder();
    }
}
