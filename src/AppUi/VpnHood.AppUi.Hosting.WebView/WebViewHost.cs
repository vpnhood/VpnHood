using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.WebView;

// Platform-neutral controller that hosts the VpnHood SPA in a web view: starts the page host it is
// handed, computes the launch URL, drives the loading/error state and loads the SPA again after the
// web view reports a failure. Only the native web-view mechanics live in the per-platform IWebView;
// the web host keeps itself alive (see IAppWebHost). Which page host is the OS host's to say - the
// app's own local web host when the app runs in the host's process (AppWebViewPageHost) - so nothing
// here assumes the app is.
//
// Lifecycle from the OS host: construct one per web view, call Start() when the host UI is created,
// OnResume() from the platform's foreground/resume hook, and Dispose() when it is torn down.
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

    // Start the page host off the UI thread (the app's own extracts the web root and binds a socket),
    // then build the web view and load the SPA back on the UI thread.
    public void Start()
    {
        Task.Run(async () => {
            try {
                _launchUrl = await _pageHost.EnsureStarted(CancellationToken.None).Vhc();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Failed to start the SPA web server.");
                _view.Post(() => _view.ShowError(ex.Message));
                return;
            }

            _view.Post(InitializeAndLoad);
        });
    }

    // Call from the platform's resume/foreground hook. The app and the web server subscribe to it.
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
                _launchUrl = await _pageHost.EnsureStarted(CancellationToken.None).Vhc();
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _view.PageLoaded -= OnPageLoaded;
        _view.LoadFailed -= OnLoadFailed;
        _view.ContentProcessGone -= OnContentProcessGone;
        _pageHost.Restarted -= OnServerRestarted;
    }
}
