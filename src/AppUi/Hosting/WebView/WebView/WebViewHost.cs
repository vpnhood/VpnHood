using Microsoft.Extensions.Logging;
using VpnHood.AppLib;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.WebView;

// Platform-neutral controller that hosts the VpnHood SPA in a web view: starts the loopback web
// server, computes the launch URL, drives the loading/error state and loads the SPA again after
// the web view reports a failure. Only the native web-view mechanics live in the per-platform
// IWebView; the server keeps itself alive (see VpnHoodAppWebHost).
//
// Lifecycle from the OS host: construct one per web view, call Start() when the host UI is created,
// OnResume() from the platform's foreground/resume hook, and Dispose() when it is torn down.
public sealed class WebViewHost : IDisposable
{
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromSeconds(1);
    private readonly IWebView _view;
    private readonly WebViewHostOptions _options;
    private VpnHoodAppWebHost? _server;
    private bool _viewInitialized;
    private bool _reloadPending;
    private bool _disposed;

    public WebViewHost(IWebView view, WebViewHostOptions? options = null)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _options = options ?? new WebViewHostOptions();

        _view.PageLoaded += OnPageLoaded;
        _view.LoadFailed += OnLoadFailed;
        _view.ContentProcessGone += OnContentProcessGone;
    }

    // Start the web host off the UI thread (it extracts the web root and binds a socket), then
    // build the web view and load the SPA back on the UI thread.
    public void Start()
    {
        Task.Run(() => {
            try {
                VpnHoodAppWebHost.Instance.Start();
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
            if (_server == null) {
                _server = VpnHoodAppWebHost.Instance;
                _server.Restarted += OnServerRestarted;
            }

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

    private Uri GetLaunchUrl()
    {
        // nocache busts the web-view cache whenever the bundled SPA changes.
        var url = new Uri($"{VpnHoodAppWebHost.Instance.Url}?nocache={VpnHoodAppWebHost.Instance.WebRootHash}");
        return _options.LaunchUrlBuilder?.Invoke(url) ?? url;
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        _view.SetLoading(false);
    }

    private void OnLoadFailed(object? sender, EventArgs e)
    {
        VhLogger.Instance.LogWarning("SPA web view load failed; reloading.");
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
                if (VpnHoodAppWebHost.IsInit)
                    await VpnHoodAppWebHost.Instance.RestartIfUnreachable().Vhc();
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
        if (_server != null) {
            _server.Restarted -= OnServerRestarted;
            _server = null;
        }
    }
}
