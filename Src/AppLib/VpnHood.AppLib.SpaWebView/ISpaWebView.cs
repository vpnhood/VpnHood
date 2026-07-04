namespace VpnHood.AppLib.SpaWebView;

// The per-platform surface of the SPA web view. Each OS implements ONLY this; all the hosting
// business logic lives in <see cref="SpaWebViewHost"/>.
//
// Threading contract: SpaWebViewHost calls the methods below on the platform UI thread (it hops
// there via <see cref="Post"/> after its background work), and it expects the events to be raised
// on the UI thread as well.
public interface ISpaWebView
{
    // Create the native web view and attach it to the platform's view hierarchy. Called once.
    void Initialize();

    // Navigate to the given url — used for the initial load and every reload.
    void Load(Uri url);

    // Reload the current content.
    void Reload();

    // Show or hide the loading indicator (spinner / loading page).
    void SetLoading(bool isLoading);

    // Show a fatal error screen: the UI could not be started or recovered.
    void ShowError(string message);

    // Marshal an action onto the platform UI thread. SpaWebViewHost uses this so its background
    // work (starting the web server) can hop back to the UI thread before touching the web view.
    void Post(Action action);

    // Raised when a navigation completed successfully.
    event EventHandler? PageLoaded;

    // Raised when a load failed. The adapter must NOT raise this for cancelled / superseded loads
    // (e.g. iOS NSUrlError.Cancelled -999, the expected result of starting a new load over an
    // in-flight one) — only for real failures, or recovery would loop against itself.
    event EventHandler<SpaLoadFailedEventArgs>? LoadFailed;

    // Raised when the web view's content process was terminated by the OS (iOS/WKWebView under
    // memory pressure). Platforms without such a concept never raise it.
    event EventHandler? ContentProcessGone;
}
