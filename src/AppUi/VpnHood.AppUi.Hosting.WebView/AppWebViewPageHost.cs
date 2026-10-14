using VpnHood.AppLib.App.WebHosting;

namespace VpnHood.AppUi.Hosting.WebView;

// The app's own web host as the page's server, where the app runs in the web view's process.
public sealed class AppWebViewPageHost(IAppWebHost webHost) : IWebViewPageHost
{
    public Task<Uri> EnsureStarted(CancellationToken cancellationToken) => webHost.EnsureStarted(cancellationToken);

    public event EventHandler? Restarted {
        add => webHost.Restarted += value;
        remove => webHost.Restarted -= value;
    }
}
