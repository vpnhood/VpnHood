using VpnHood.AppLib.App.WebHosting;

namespace VpnHood.AppUi.Hosting.WebView;

/// <summary>Provides the app's own web host as the page's server, where the app runs in the web view's process.</summary>
/// <param name="webHost">The app's web host.</param>
public sealed class AppWebViewPageHost(IAppWebHost webHost) : IWebViewPageHost
{
    /// <inheritdoc/>
    public Task<Uri> EnsureStarted(CancellationToken cancellationToken) => webHost.EnsureStarted(cancellationToken);

    /// <inheritdoc/>
    public event EventHandler? Restarted {
        add => webHost.Restarted += value;
        remove => webHost.Restarted -= value;
    }
}
