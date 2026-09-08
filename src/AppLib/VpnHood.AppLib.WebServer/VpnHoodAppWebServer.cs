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
    private Timer? _watchdogTimer;
    private bool _disposed;
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    public Uri Url { get; }

    public string SpaHash => _spaHash ?? throw new InvalidOperationException($"{nameof(SpaHash)} is not initialized");
    public bool UseHostName { get; set; }

    // The listener's own state. CavemanTcp clears it when its accept loop exits, so false means
    // the listener is gone (or was never started).
    public bool IsListening => _server?.IsListening == true;

    // Raised after a successful restart, outside the server lock so UI subscribers can dispatch.
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
        AppUiContext.OnResumed += AppUiContextOnResumed;
    }

    // iOS suspends the process and can close the loopback socket meanwhile while the accept loop
    // still believes it is listening, so the flag alone is not enough there: one real connect on
    // every resume, off the UI thread. The host reloads the SPA if the server restarts.
    private void AppUiContextOnResumed(object? sender, EventArgs e)
    {
        Task.Run(() => {
            try {
                RestartIfUnreachable();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "SPA web server resume check failed.");
            }
        });
    }

    private void SettingsServiceOnBeforeSave(object? sender, EventArgs e)
    {
        if (_app.SettingsService.OldUserSettings.AllowRemoteAccess !=
            _app.SettingsService.UserSettings.AllowRemoteAccess)
            Restart();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            AppUiContext.OnResumed -= AppUiContextOnResumed;
            // Under the lock so a watchdog tick that already fired can't restart the stopped server.
            lock (_serverLock) {
                _disposed = true;
                _watchdogTimer?.Dispose();
                _watchdogTimer = null;
                Stop();
            }
        }

        base.Dispose(disposing);
    }

    public static VpnHoodAppWebServer Init(VpnHoodApp app, WebServerOptions? options = null)
    {
        var ret = new VpnHoodAppWebServer(app, options ?? new WebServerOptions());
        ret.Start();
        ret._watchdogTimer = new Timer(_ => ret.RestartIfDown(), null, WatchdogInterval, WatchdogInterval);
        return ret;
    }

    // Watchdog: put the listener back when it has died. It only reads the listener's own state, so
    // a healthy server is never restarted. Notify the host so assets interrupted by the outage
    // are loaded again, even when the main document had already finished loading.
    private void RestartIfDown()
    {
        try {
            lock (_serverLock) {
                if (_watchdogTimer == null || IsListening)
                    return; // disposed, or alive

                VhLogger.Instance.LogWarning("SPA web server listener is down; restarting it.");
                Stop();
                Start();
            }
            Restarted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "SPA web server watchdog failed.");
        }
    }

    // Only on concrete signals (resume, a web view that failed to connect), never periodically: a
    // probe that times out on a busy system would restart a healthy server.
    public void RestartIfUnreachable()
    {
        lock (_serverLock) {
            if (_disposed || IsReachable())
                return;

            VhLogger.Instance.LogWarning("SPA web server is not reachable; restarting it.");
            Stop();
            Start();
        }
        Restarted?.Invoke(this, EventArgs.Empty);
    }

    private bool IsReachable()
    {
        try {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(ProbeTimeout);
            client.ConnectAsync(IPAddress.Loopback, Url.Port, cts.Token).AsTask().GetAwaiter().GetResult();
            return client.Connected;
        }
        catch {
            return false;
        }
    }

    // The lock serializes the state transitions: initial start, settings-save restart, the watchdog
    // and the web view's recovery can all drive them from different threads. Monitor is re-entrant,
    // so Restart()'s nested Stop()/Start() calls are fine.
    public void Start()
    {
        lock (_serverLock) {
            VhLogger.Instance.LogInformation("Starting web server...");

            if (_server != null) {
                _server.Start();
                return;
            }

            // The listener socket is bound and listening before Start() returns, so callers can
            // point a web view at Url immediately.
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
            if (_disposed)
                return;
            Stop();
            Start();
        }
        Restarted?.Invoke(this, EventArgs.Empty);
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

        return [.. ipAddresses];
    }
}
