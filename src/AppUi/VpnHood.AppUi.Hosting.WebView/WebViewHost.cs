using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.AppLib.Abstractions.UiContexts;

namespace VpnHood.AppUi.Hosting.WebView;

/// <summary>
/// Provides the platform-neutral control of a web view that hosts the app's web UI: it starts the page host it is
/// handed, works out the launch address, drives the loading and error states, and loads the UI again after the web view
/// reports a failure.
/// </summary>
/// <remarks>
/// Only the native web view's mechanics are the platform's, in its <see cref="IWebView"/>. A platform host makes one per
/// web view, calls <see cref="Start"/> when its UI is created, <see cref="OnResume"/> from its resume hook, and
/// <see cref="Dispose"/> when it is torn down. A failed load is tried again: the page host is asked for its address
/// again, which a server the platform owns checks, and after a second's wait the page loads again; another failure
/// starts another round.
/// </remarks>
public sealed class WebViewHost : IDisposable
{
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromSeconds(1);
    private readonly IWebView _view;
    private readonly IWebViewPageHost _pageHost;
    private readonly WebViewHostOptions _options;
    private Uri? _launchUrl;
    private bool _viewInitialized;
    private bool _reloadPending;
    private bool _isLoadFailing;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="WebViewHost"/> class.</summary>
    /// <param name="view">The platform's web view.</param>
    /// <param name="pageHost">The server the page comes from.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="view"/> or <paramref name="pageHost"/> is <see langword="null"/>.
    /// </exception>
    public WebViewHost(IWebView view, IWebViewPageHost pageHost, WebViewHostOptions? options = null)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _pageHost = pageHost ?? throw new ArgumentNullException(nameof(pageHost));
        _options = options ?? new WebViewHostOptions();

        _view.PageLoaded += OnPageLoaded;
        _view.LoadFailed += OnLoadFailed;
        _view.ContentProcessGone += OnContentProcessGone;
        _pageHost.Restarted += OnServerRestarted;
    }

    /// <summary>
    /// Starts the page host off the UI thread, then makes the web view and loads the UI on the UI thread; where the page
    /// host fails to start, the failure goes to the web view's <see cref="IWebView.ShowError"/>.
    /// </summary>
    public void Start()
    {
        Task.Run(async () => {
            try {
                _launchUrl = await _pageHost.EnsureStarted(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Failed to start the SPA web server.");
                _view.Post(() => _view.ShowError(ex.Message));
                return;
            }

            _view.Post(InitializeAndLoad);
        });
    }

    /// <summary>Tells the app's UI context the app is in front again; a platform host calls it from its resume hook.</summary>
    public void OnResume()
    {
        AppUiContext.NotifyResumed();
    }

    private void InitializeAndLoad()
    {
        if (_disposed)
            return;

        try {
            if (!_viewInitialized) {
                _view.Initialize();
                _viewInitialized = true;
            }

            LoadSpa();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to initialize the SPA web view.");
            _view.ShowError(ex.Message);
        }
    }

    private void LoadSpa()
    {
        _view.SetLoading(true);
        _view.Load(GetLaunchUrl());
    }

    // The address of the last EnsureStarted; the host carries the web root's hash in it, so a web
    // view holding an older build is not served it.
    private Uri GetLaunchUrl()
    {
        var url = _launchUrl ?? throw new InvalidOperationException("The web host has not been started.");
        return _options.LaunchUrlBuilder?.Invoke(url) ?? url;
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        _isLoadFailing = false;
        _view.SetLoading(false);
    }

    // A warning once per outage: the reload repeats every second while the page is unreachable, and
    // its repeats are debug.
    private void OnLoadFailed(object? sender, EventArgs e)
    {
        VhLogger.Instance.Log(_isLoadFailing ? LogLevel.Debug : LogLevel.Warning, "SPA web view load failed; reloading.");
        _isLoadFailing = true;
        ReloadAfterDelay();
    }

    private void OnServerRestarted(object? sender, EventArgs e)
    {
        _view.Post(() => {
            if (!_disposed && _viewInitialized)
                ReloadAfterDelay();
        });
    }

    private void OnContentProcessGone(object? sender, EventArgs e)
    {
        VhLogger.Instance.LogWarning("SPA web view content process terminated; reloading.");
        ReloadAfterDelay();
    }

    // Load the SPA again after a short delay. A failed load is a concrete "unreachable" signal, so
    // the server gets one real connect check first (its own state flag can lie after an iOS
    // suspension); a listener that really is gone is rebound before the reload. The delay is the
    // only bound on retries: it keeps a still-failing load at one attempt per second instead of
    // dead-ending the user on an error screen. Overlapping signals collapse into one reload.
    private void ReloadAfterDelay()
    {
        if (_disposed || _reloadPending)
            return;

        _reloadPending = true;
        _view.SetLoading(true);
        Task.Run(async () => {
            try {
                // one real connect check, and a rebind when the listener really is gone
                _launchUrl = await _pageHost.EnsureStarted(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Failed to check the SPA web server after a failed load.");
            }

            await Task.Delay(ReloadDelay);
            _view.Post(() => {
                _reloadPending = false;
                if (!_disposed)
                    LoadSpa();
            });
        });
    }

    /// <inheritdoc/>
    /// <remarks>It stops following the web view's and the page host's events; a second call does nothing.</remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        _view.PageLoaded -= OnPageLoaded;
        _view.LoadFailed -= OnLoadFailed;
        _view.ContentProcessGone -= OnContentProcessGone;
        _pageHost.Restarted -= OnServerRestarted;
    }
}
