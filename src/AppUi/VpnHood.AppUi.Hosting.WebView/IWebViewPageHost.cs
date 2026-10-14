namespace VpnHood.AppUi.Hosting.WebView;

// What WebViewHost needs of the server its page comes from: the address to load, and word when the
// server is bound again. The app's own web host where the app runs in this process
// (AppWebViewPageHost); on a desktop, the address the service hands the window.
public interface IWebViewPageHost
{
    // The address, the server answering by the time it returns. A later call is the caller's own
    // "unreachable" signal, which may cost the server a real connect check.
    Task<Uri> EnsureStarted(CancellationToken cancellationToken);

    // Bound again under a loaded page, maybe at another address, so what the page loaded may be half
    // fetched. From any thread.
    event EventHandler? Restarted;
}
