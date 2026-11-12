namespace VpnHood.AppUi.Hosting.WebView;

/// <summary>
/// Defines the server the web view's page comes from: the address to load, and word when the server is bound again.
/// </summary>
/// <remarks>
/// It is the app's own web host where the app runs in this process (<see cref="AppWebViewPageHost"/>), and on a desktop
/// the address the service hands the window.
/// </remarks>
public interface IWebViewPageHost
{
    /// <summary>
    /// Gets the address to load: a server the platform owns is started, or checked, by the time it returns; a desktop's is
    /// the address the service last handed over.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the address.</returns>
    /// <remarks>
    /// A later call is the caller's own sign that the server is unreachable, which may cost the server a real connect check.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The platform's own server could not bind.</exception>
    /// <exception cref="ObjectDisposedException">The platform's own server was disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<Uri> EnsureStarted(CancellationToken cancellationToken);

    /// <summary>
    /// Occurs when the server is bound again under a loaded page, maybe at another address, so what the page loaded may be
    /// half fetched; it may be raised on any thread.
    /// </summary>
    event EventHandler? Restarted;
}
