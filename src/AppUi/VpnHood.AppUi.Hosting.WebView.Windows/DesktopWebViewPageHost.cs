using VpnHood.AppUi.Hosting.Desktop.Abstractions;

namespace VpnHood.AppUi.Hosting.WebView.Windows;

// The page at the address the desktop host hands the window: the service's, or in dev the app's own
// in this process. A rebind moves it, and the move is a Restarted, on which WebViewHost loads the
// page again.
internal sealed class DesktopWebViewPageHost(IDesktopApiUrlProvider apiUrlProvider) : IWebViewPageHost
{
    public Task<Uri> EnsureStarted(CancellationToken cancellationToken) => Task.FromResult(apiUrlProvider.Current);

    public event EventHandler? Restarted {
        add => apiUrlProvider.Changed += value;
        remove => apiUrlProvider.Changed -= value;
    }
}
