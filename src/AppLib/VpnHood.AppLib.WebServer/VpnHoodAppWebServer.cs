using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.WebServer.Controllers;
using VpnHood.AppLib.WebServer.Helpers;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;

namespace VpnHood.AppLib.WebServer;

public class VpnHoodAppWebServer : Singleton<VpnHoodAppWebServer>, IDisposable
{
    private string? _indexHtml;
    private WebserverLite? _server;
    private string? _spaHash;
    private string? _spaPath;
    private readonly bool _isDebugMode;
    private readonly Lock _serverLock = new();
    private Timer? _healthTimer;
    private int _healthCheckBusy;
    private int _inconclusiveProbes;
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(500);
    private const int ProbeAttempts = 3;
    private const int MaxInconclusiveProbes = 3;
    private static readonly TimeSpan ProbeRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan RestartReadyTimeout = TimeSpan.FromSeconds(3);
    public Uri Url { get; }

    public string SpaHash => _spaHash ?? throw new InvalidOperationException($"{nameof(SpaHash)} is not initialized");
    public bool UseHostName { get; set; }

    // True only while the underlying loopback listener is actually accepting connections.
    public bool IsListening => _server?.IsListening == true;

    // Raised (off the caller's thread) after the server had to (re)start itself — e.g. the listener
    // was found dead on resume. Platform UI subscribes to this to reload its WebView, whose old
    // connections to the previous listener are now gone.
    public event EventHandler? Restarted;

    private readonly VpnHoodApp _app;

    private VpnHoodAppWebServer(VpnHoodApp app, WebServerOptions options)
    {
        _app = app;
        _isDebugMode = app.Features.IsDebugMode;
        var defaultPort = app.Features.WebUiPort ?? 9090;
        var host = IPAddress.Loopback; // fallback safe default; adjust if you have AllowRemoteAccess
        var endPoint = VhUtils.GetFreeTcpEndPoint(host, defaultPort);
        Url = options.Url ?? new Uri($"http://{endPoint}");
        app.SettingsService.BeforeSave += SettingsServiceOnBeforeSave;
        // Self-heal when the app returns to the foreground: iOS (and, less often, other platforms)
        // can tear the loopback listener down while the app is in the background, with no notification.
        AppUiContext.OnResumed += AppUiContextOnResumed;
    }

    private void SettingsServiceOnBeforeSave(object? sender, EventArgs e)
    {
        if (_app.SettingsService.OldUserSettings.AllowRemoteAccess !=
            _app.SettingsService.UserSettings.AllowRemoteAccess)
            Restart();
    }

    private void AppUiContextOnResumed(object? sender, EventArgs e)
    {
        // Immediate authoritative re-check on foreground (don't wait for the next timer tick). Off
        // the UI thread — the probe/rebind must not block it.
        Task.Run(RunHealthCheck);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            AppUiContext.OnResumed -= AppUiContextOnResumed;
            _healthTimer?.Dispose();
            _healthTimer = null;
            Stop();
        }

        base.Dispose(disposing);
    }

    public static VpnHoodAppWebServer Init(VpnHoodApp app, WebServerOptions? options = null)
    {
        var ret = new VpnHoodAppWebServer(app, options ?? new WebServerOptions());
        ret.Start();
        // Don't hand the URL to a web view until the accept loop is actually taking connections;
        // Start() can return inside that window and the first page load would be refused.
        ret.WaitUntilReachable();
        ret.StartHealthMonitor();
        return ret;
    }

    // A 1-second watchdog that keeps the loopback listener alive on every platform without relying
    // on any lifecycle hook. On iOS the host app is suspended in the background, so this timer is
    // frozen there (zero background cost) and fires again on resume; AppUiContext.OnResumed also
    // kicks an immediate check so recovery isn't delayed by a tick.
    private void StartHealthMonitor()
    {
        _healthTimer ??= new Timer(_ => RunHealthCheck(), null,
            HealthCheckInterval, HealthCheckInterval);
    }

    private void RunHealthCheck()
    {
        // Skip if the previous check (possibly a restart) is still running.
        if (Interlocked.CompareExchange(ref _healthCheckBusy, 1, 0) != 0)
            return;

        try {
            HealIfUnreachable();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "SPA web server health check failed.");
        }
        finally {
            Interlocked.Exchange(ref _healthCheckBusy, 0);
        }
    }

    private enum ProbeResult
    {
        Connected,
        Refused, // nothing is accepting on the port — authoritative death
        Inconclusive // timed out or failed oddly — the listener may still be fine on a busy system
    }

    // Restart the server if it is not actually reachable. Returns true if it had to restart.
    private bool HealIfUnreachable()
    {
        if (IsListening) {
            switch (Probe()) {
                case ProbeResult.Connected:
                    _inconclusiveProbes = 0;
                    return false;

                case ProbeResult.Inconclusive when ++_inconclusiveProbes < MaxInconclusiveProbes:
                    // A timeout is not proof the listener is dead: right after the OS thaws a frozen
                    // process (Android's cached-apps freezer on resume) loopback connects can time out
                    // for a moment even though the server is healthy. Restarting a healthy server here
                    // is what strands the UI on a dead page, so act only on consecutive inconclusive
                    // checks. A genuinely dead listener refuses (fast) and never reaches this branch.
                    VhLogger.Instance.LogWarning(
                        "SPA web server probe was inconclusive ({Count}/{Max}); deferring restart.",
                        _inconclusiveProbes, MaxInconclusiveProbes);
                    return false;
            }
        }

        _inconclusiveProbes = 0;
        lock (_serverLock) {
            VhLogger.Instance.LogWarning("SPA web server is not reachable; restarting it.");
            Stop();
            Start();
        }

        // Announce the restart only once the new listener actually accepts. Start() can return
        // before the accept loop is bound; a reload sent into that window gets connection-refused
        // and lands the web view on an error page. Waiting also happens outside the lock, and the
        // _healthCheckBusy guard keeps the next timer tick from probing the half-started listener.
        WaitUntilReachable();

        // Notify outside the lock so subscribers can't deadlock against a state transition.
        Restarted?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // Authoritative liveness check. IsListening is only the server's own belief and can stay true
    // after the OS tears the socket down, so when it claims to be up we confirm with a real loopback
    // connect. The probe is done outside _serverLock so a slow connect never blocks Start/Stop.
    // Retries a few times before giving a verdict: a listener that has just (re)started can briefly
    // refuse connects in the window between Start() returning and its accept loop binding, and it
    // must not be torn down for that transient (it caused a spurious restart right after launch).
    private ProbeResult Probe()
    {
        var sawInconclusive = false;
        for (var attempt = 0; attempt < ProbeAttempts; attempt++) {
            if (attempt > 0)
                Thread.Sleep(ProbeRetryDelay);

            var result = TryConnect();
            if (result == ProbeResult.Connected)
                return ProbeResult.Connected;
            sawInconclusive |= result == ProbeResult.Inconclusive;
        }

        return sawInconclusive ? ProbeResult.Inconclusive : ProbeResult.Refused;
    }

    private ProbeResult TryConnect()
    {
        // Cancellation (not Wait(timeout)) so the connect task is always observed on timeout — a
        // stray faulted task here would surface as an unhandled-task-exception SIGABRT.
        try {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(ProbeTimeout);
            client.ConnectAsync(IPAddress.Loopback, Url.Port, cts.Token).AsTask().GetAwaiter().GetResult();
            return client.Connected ? ProbeResult.Connected : ProbeResult.Refused;
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionRefused
                                             or SocketError.ConnectionReset) {
            return ProbeResult.Refused;
        }
        catch {
            return ProbeResult.Inconclusive;
        }
    }

    // Block (briefly) until the just-(re)started listener accepts a loopback connect, so callers can
    // safely point a web view at it the moment this returns. Bounded: if the listener is genuinely
    // broken the health monitor will keep healing it, and the platforms recover failed loads.
    private void WaitUntilReachable()
    {
        var deadline = Environment.TickCount64 + (long)RestartReadyTimeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline) {
            if (TryConnect() == ProbeResult.Connected)
                return;
            Thread.Sleep(ProbeRetryDelay);
        }

        VhLogger.Instance.LogError("SPA web server is still not reachable after being started.");
    }

    // The lock serializes the state transitions now that several triggers can drive them
    // concurrently (initial start on the UI thread, settings-save on the UI thread, and the
    // resume self-heal on a background thread). Monitor is re-entrant, so Restart()'s nested
    // Stop()/Start() calls are fine.
    public void Start()
    {
        lock (_serverLock) {
            VhLogger.Instance.LogInformation("Starting web server...");

            if (_server != null) {
                _server.Start();
                return;
            }

            _server = CreateWebServer();
            _server.Start();
            VhLogger.Instance.LogInformation("Web server has been started on {Url}", Url);
        }
    }

    public void Stop()
    {
        lock (_serverLock) {
            var oldServer = _server;
            if (oldServer == null)
                return;

            VhLogger.Instance.LogInformation("Stopping web server...");
            oldServer.TryStop();
            oldServer.Dispose();
            _server = null;
        }
    }

    public void Restart()
    {
        lock (_serverLock) {
            Stop();
            Start();
        }

        // Callers reload the web view right after a restart, so don't return before the new accept
        // loop is actually taking connections (outside the lock; see WaitUntilReachable).
        WaitUntilReachable();
    }

    // Make sure the loopback listener is actually reachable, restarting it if it is not. iOS can
    // tear the listener socket down while the host app is in the background (or the process is
    // memory-pressured), leaving a dead server the WKWebView SPA can no longer reach ("cannot
    // connect to the internal web server"). This is normally driven automatically by the 1s health
    // monitor and by AppUiContext.OnResumed, but platforms can also call it directly after a page
    // load fails. Returns true if the server had to be (re)started; the Restarted event fires then.
    public bool EnsureStarted()
    {
        return HealIfUnreachable();
    }

    private string GetSpaPath()
    {
        if (_spaPath != null)
            return _spaPath; // do not extract in same instance

        if (_app.Resources.SpaZipData is null)
            throw new InvalidOperationException("SpaZipData resource is required to run web server for SPA.");

        using var memZipStream = new MemoryStream(_app.Resources.SpaZipData);
        memZipStream.Seek(0, SeekOrigin.Begin);
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(memZipStream);
        _spaHash = BitConverter.ToString(hash).Replace("-", "");

        var spaFolderPath = Path.Combine(_app.StorageFolderPath, "Temp", "SPA");
        var spaPath = Path.Combine(spaFolderPath, _spaHash);
        var htmlPath = Path.Combine(spaPath, "index.html");
        if (!File.Exists(htmlPath)) {
            if (Directory.Exists(spaFolderPath))
                VhUtils.TryInvoke("Delete old SPA folder", () => Directory.Delete(spaFolderPath, true));
            memZipStream.Seek(0, SeekOrigin.Begin);
            using var zipArchive = new ZipArchive(memZipStream);
            zipArchive.ExtractToDirectory(spaPath, true);
        }

        _spaPath = spaPath;
        return spaPath;
    }

    private WebserverLite CreateWebServer()
    {
        var spaPath = GetSpaPath();
        _indexHtml = File.ReadAllText(Path.Combine(spaPath, "index.html"));

        var host = _app.UserSettings.AllowRemoteAccess ? IPAddress.Any.ToString() : Url.Host;
        var settings = new WebserverSettings(host, Url.Port);
        var server = new WebserverLite(settings, ctx => DefaultRoute(ctx, spaPath));

        // Initialize API routes through controllers - CORS is handled centrally in the route mapper
        server
            .AddRouteMapper(_isDebugMode)
            .AddController(new AppController(_app))
            .AddController(new ClientProfileController(_app))
            .AddController(new AccountController(_app))
            .AddController(new BillingController(_app))
            .AddController(new IntentsController(_app))
            .AddController(new ProxyEndPointController(_app));

        return server;
    }

    private static Task ServeFile(HttpContextBase context, string fullPath)
    {
        var contentType = MimeTypeUtils.GetContentType(fullPath);
        context.Response.ContentType = contentType;
        return context.Response.Send(File.ReadAllBytes(fullPath));
    }

    private async Task DefaultRoute(HttpContextBase context, string spaPath)
    {
        if (_indexHtml == null)
            throw new InvalidOperationException($"{nameof(_indexHtml)} is not initialized");

        // Add CORS centrally for default route
        CorsMiddleware.AddCors(context, _isDebugMode);

        if (context.Request.Url.RawWithoutQuery.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await context.Response.Send();
            return;
        }

        // use LocalPath for security reasons (Url.PathAndQuery can contain double dots)
        var localPath = context.Request.Url.Uri.LocalPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(spaPath, localPath);
        if (File.Exists(fullPath)) {
            await ServeFile(context, fullPath);
            return;
        }

        context.Response.ContentType = "text/html";
        await context.Response.Send(_indexHtml);
    }

    // ReSharper disable once UnusedMember.Local
    private static IEnumerable<IPAddress> GetAllPublicIp4()
    {
        var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x.OperationalStatus is OperationalStatus.Up && x.Supports(NetworkInterfaceComponent.IPv4) &
                x.NetworkInterfaceType is not NetworkInterfaceType.Loopback);

        var ipAddresses = new List<IPAddress>();
        foreach (var networkInterface in networkInterfaces) {
            var ipProperties = networkInterface.GetIPProperties();
            var uniCastAddresses = ipProperties.UnicastAddresses;
            var ips = uniCastAddresses.Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(x => x.Address);
            ipAddresses.AddRange(ips);
        }

        return ipAddresses.ToArray();
    }
}