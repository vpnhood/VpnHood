namespace VpnHood.AppLib.SpaWebView;

// The per-platform surface of the SPA web view. Each OS implements ONLY this: the native web-view
// mechanics, and reporting what happened to it. SpaWebViewHost decides what to do about it.
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

    // Show or hide the loading indicator (spinner / loading page).
    void SetLoading(bool isLoading);

    // Show a fatal error screen: the UI could not be started.
    void ShowError(string message);

    // Marshal an action onto the platform UI thread. SpaWebViewHost uses this so its background
    // work (starting the web server) can hop back to the UI thread before touching the web view.
    void Post(Action action);

    // Raised when a navigation completed successfully.
    event EventHandler? PageLoaded;

    // Raised when the main document could not be loaded. The adapter must NOT raise this for
    // cancelled / superseded loads (e.g. iOS NSUrlError.Cancelled -999, the expected result of
    // starting a new load over an in-flight one) — only for real failures.
    event EventHandler? LoadFailed;

    // Raised when the web view's content process was terminated by the OS and the view is ready to
    // be loaded again (Android rebuilds its WebView first). Platforms without such a concept never
    // raise it.
    event EventHandler? ContentProcessGone;
}
