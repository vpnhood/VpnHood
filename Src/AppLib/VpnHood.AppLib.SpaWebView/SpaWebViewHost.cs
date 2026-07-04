using Microsoft.Extensions.Logging;
using VpnHood.AppLib.WebServer;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.SpaWebView;

// Platform-neutral controller that hosts the VpnHood SPA in a web view. It owns all the hosting
// business logic — starting the loopback web server, computing the launch URL, driving the
// loading/error state, reloading when the server self-heals, and the bounded recovery state
// machine — and delegates only the native web-view mechanics to a per-platform ISpaWebView.
//
// Lifecycle from the OS host: construct one per web view, call Start() when the host UI is created,
// OnResume() from the platform's foreground/resume hook, and Dispose() when it is torn down.
public sealed class SpaWebViewHost : IDisposable
{
    private readonly ISpaWebView _view;
    private readonly SpaWebViewHostOptions _options;

    // All the following are touched only on the UI thread (Start's background work hops back via
    // _view.Post, and the ISpaWebView events are contracted to be raised on the UI thread).
    private int _recoveryAttempts;
    private bool _serverHooked;
    private bool _viewInitialized;
    private bool _disposed;

    public SpaWebViewHost(ISpaWebView view, SpaWebViewHostOptions? options = null)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _options = options ?? new SpaWebViewHostOptions();

        _view.PageLoaded += OnPageLoaded;
        _view.LoadFailed += OnLoadFailed;
        _view.ContentProcessGone += OnContentProcessGone;
    }

    // Bring up the web server off the UI thread (it extracts the SPA zip and binds a socket), then
    // build the web view and load the SPA back on the UI thread.
    public void Start()
    {
        Task.Run(() => {
            try {
                if (!VpnHoodAppWebServer.IsInit)
                    VpnHoodAppWebServer.Init();

                // Reload the view whenever the server self-heals a torn-down listener (subscribe once).
                if (!_serverHooked) {
                    VpnHoodAppWebServer.Instance.Restarted += OnServerRestarted;
                    _serverHooked = true;
                }
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Failed to start the SPA web server.");
                _view.Post(() => _view.ShowError(ex.Message));
                return;
            }

            _view.Post(InitializeAndLoad);
        });
    }

    // Call from the platform's resume/foreground hook. The web server self-heals its listener (see
    // VpnHoodAppWebServer); if it had to restart, OnServerRestarted reloads the view.
    public void OnResume()
    {
        AppUiContext.NotifyResumed();
    }

    private void InitializeAndLoad()
    {
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

    private Uri GetLaunchUrl()
    {
        // nocache busts the web-view cache whenever the bundled SPA changes.
        var url = new Uri($"{VpnHoodAppWebServer.Instance.Url}?nocache={VpnHoodAppWebServer.Instance.SpaHash}");
        return _options.LaunchUrlBuilder?.Invoke(url) ?? url;
    }

    private void OnServerRestarted(object? sender, EventArgs e)
    {
        // The server self-healed a torn-down listener; the view's old connections are gone, so reload.
        _view.Post(LoadSpa);
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        _view.SetLoading(false);
        _recoveryAttempts = 0; // a successful load means the server is reachable again
    }

    private void OnLoadFailed(object? sender, SpaLoadFailedEventArgs e)
    {
        VhLogger.Instance.LogWarning("SPA web view load failed (initialConnect={Initial}).",
            e.DuringInitialConnect);
        // A failed initial connection proves the current listener is unusable even if it still
        // reports it is up, so force a rebind; otherwise a reload is enough.
        Recover(forceServerRestart: e.DuringInitialConnect);
    }

    private void OnContentProcessGone(object? sender, EventArgs e)
    {
        VhLogger.Instance.LogWarning("SPA web view content process terminated; recovering.");
        Recover(forceServerRestart: false);
    }

    private void Recover(bool forceServerRestart)
    {
        if (_recoveryAttempts >= _options.MaxRecoveryAttempts) {
            VhLogger.Instance.LogError("SPA web view could not be recovered after {Attempts} attempts.",
                _recoveryAttempts);
            _view.ShowError(_options.ServerNotRespondingMessage);
            return;
        }

        _recoveryAttempts++;
        Task.Run(() => {
            try {
                if (VpnHoodAppWebServer.IsInit) {
                    if (forceServerRestart)
                        VpnHoodAppWebServer.Instance.Restart();
                    else
                        VpnHoodAppWebServer.Instance.EnsureStarted();
                }
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Failed to recover the SPA web server.");
            }

            _view.Post(LoadSpa);
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

        if (_serverHooked && VpnHoodAppWebServer.IsInit) {
            VpnHoodAppWebServer.Instance.Restarted -= OnServerRestarted;
            _serverHooked = false;
        }
    }
}
