namespace VpnHood.AppUi.Hosting.WebView;

/// <summary>
/// Defines a platform's web view for the app's web UI: the native web view's mechanics, and what happened to it;
/// <see cref="WebViewHost"/> decides what to do about it.
/// </summary>
/// <remarks>
/// <see cref="WebViewHost"/> calls the methods on the platform's UI thread, hopping there through <see cref="Post"/>,
/// which it calls from any thread; it expects the events on the UI thread too.
/// </remarks>
public interface IWebView
{
    /// <summary>
    /// Creates the native web view, once; a platform may attach it to its view later, as Android after the first load, or
    /// finish making it asynchronously, as Windows.
    /// </summary>
    void Initialize();

    /// <summary>Navigates to an address, for the first load and every reload.</summary>
    /// <param name="url">The address.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="url"/> is relative, for a web view that reads the address's token.
    /// </exception>
    void Load(Uri url);

    /// <summary>
    /// Tells the web view whether the UI is loading, which a platform shows as it can: Android keeps its loading screen
    /// until the page loads, so only the end changes it.
    /// </summary>
    /// <param name="isLoading">
    /// <see langword="true"/> while the UI is loading; <see langword="false"/> when the loading has ended.
    /// </param>
    void SetLoading(bool isLoading);

    /// <summary>
    /// Shows that the UI could not be started: a fatal error screen, or another way, as Windows' opening of the page in the
    /// browser.
    /// </summary>
    /// <param name="message">The error's message.</param>
    void ShowError(string message);

    /// <summary>Runs an action on the platform's UI thread; it may be called from any thread.</summary>
    /// <param name="action">The action.</param>
    void Post(Action action);

    /// <summary>Occurs when a navigation completed.</summary>
    event EventHandler? PageLoaded;

    /// <summary>Occurs when the main document could not be loaded.</summary>
    /// <remarks>
    /// It is raised for real failures only, not for a canceled or superseded load, as iOS's <c>NSURLErrorCancelled</c>
    /// (-999) when a new load starts over one in flight.
    /// </remarks>
    event EventHandler? LoadFailed;

    /// <summary>
    /// Occurs when the system ended the web view's content process and the view is ready to load again, as Android after
    /// it rebuilds its web view; a platform without such a process never raises it.
    /// </summary>
    event EventHandler? ContentProcessGone;
}
