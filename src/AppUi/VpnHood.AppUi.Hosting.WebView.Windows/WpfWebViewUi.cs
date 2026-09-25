using System.IO;
using System.Windows;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Branding;
using VpnHood.AppUi.Hosting.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppUi.Hosting.WebView.Windows;

// The SPA in a WPF window over WebView2, as a desktop head names its UI (CliHeadParams.Ui): the page
// the app's web host serves - the service's, at the address the host hands over - with the window's
// look read from the app and from the UI's store. It runs WPF's own Application on the host's STA
// main thread until the run is cancelled, or, where no tray keeps it, until the window closes.
public class WpfWebViewUi : IDesktopUi
{
    public void Run(DesktopUiParams uiParams, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        // What the window draws with, read before it is made: a window that changes colour under the
        // person is seen, one that appears a moment later is not. The calls do not come back to this
        // thread, which has no loop running yet.
        var info = uiParams.Api.App.GetInfo(cancellationToken).GetAwaiter().GetResult();
        var resources = new AppResources();
        if (uiParams.UiAssetProvider != null)
            AppBranding.LoadAsync(resources, uiParams.UiAssetProvider, info.Features.UiTheme).GetAwaiter().GetResult();

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new VpnHoodWpfMainWindow(new WpfWindowParams {
            Api = uiParams.Api,
            WebHost = new ExternalAppWebHost(uiParams.WebUrl),
            AppName = info.Features.AppName,
            IsTv = info.Features.IsTv,
            Resources = resources,
            WebViewDataPath = Path.Combine(uiParams.UiDataPath, "WebView2"),
            ExitOnClose = uiParams.ExitOnClose,
            StartHidden = uiParams.StartHidden
        });

        if (uiParams.ExitOnClose)
            window.Closed += (_, _) => application.Shutdown();

        AppUiContext.Context = new WpfUiContext(window);
        using var registration = cancellationToken.Register(() =>
            application.Dispatcher.BeginInvoke(() => application.Shutdown()));

        if (!uiParams.StartHidden)
            window.Show();

        application.Run();
        AppUiContext.Context = null;
    }
}
