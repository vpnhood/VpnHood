using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Utils;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Controllers;
using VpnHood.AppLib.WebServer.Helpers;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;

namespace VpnHood.AppLib.WebServer;

// Two listeners serving the same SPA and API, differing only in where they bind and who owns their
// life. The primary is the app's own web view's: loopback (all interfaces for a developer), alive
// from Init to Dispose. The remote one is the pairing screen's: all interfaces, alive from
// StartRemoteAccess to StopRemoteAccess. The listener state machine itself is WebServerListener,
// once; both get the same recovery from here.
public class VpnHoodAppWebServer : Singleton<VpnHoodAppWebServer>, IDisposable
{
    private string? _indexHtml;
    private string? _spaHash;
    private string? _spaPath;
    private readonly bool _isDeveloperRemoteAccess;
    private readonly WebServerListener _primary;
    private WebServerListener? _remote;
    private int _remotePort;
    private IReadOnlyList<Uri> _remoteAccessUrls = [];

    // Presence, not sessions: the remote SPA polls every second, so an address seen within the
    // window is a device that is on, and a closed tab ages out. Keyed by address, so two browsers
    // on one phone count once. Fed by every request on every listener, since a developer's
    // always-on primary serves remote clients too.
    private readonly ConcurrentDictionary<IPAddress, DateTime> _remoteClients = new();
    private static readonly TimeSpan PresenceWindow = TimeSpan.FromSeconds(5);
    private readonly Lock _lock = new(); // guards _remote and _disposed; each listener locks itself
    private Timer? _watchdogTimer;
    private bool _disposed;
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(5);

    // The address the app's own web view loads: always loopback. Whether other devices can reach
    // the app is a property of the listeners, never of this address.
    public Uri Url { get; }

    public string SpaHash => _spaHash ?? throw new InvalidOperationException($"{nameof(SpaHash)} is not initialized");
    public bool UseHostName { get; set; }
    public bool IsListening => _primary.IsListening;

    // Held by a pairing screen, or permanent because the primary itself is on every interface.
    public bool IsRemoteAccessActive => _isDeveloperRemoteAccess || _remote != null;

    // Read by the pairing screen while it is open; nothing here touches the network, the
    // addresses are those of the last start.
    public RemoteAccessState RemoteAccessState => new() {
        IsActive = IsRemoteAccessActive,
        IsAlwaysOn = _isDeveloperRemoteAccess,
        Urls = _remoteAccessUrls,
        ConnectedDevices = GetConnectedDevices()
    };

    private IPAddress[] GetConnectedDevices()
    {
        var threshold = DateTime.UtcNow - PresenceWindow;
        foreach (var stale in _remoteClients.Where(x => x.Value < threshold).Select(x => x.Key).ToArray())
            _remoteClients.TryRemove(stale, out _);

        return [.. _remoteClients.Keys];
    }

    // Watson's pre-routing hook, on both listeners. Never handles the request itself.
    private Task<bool> NoteRemoteClient(HttpContextBase ctx)
    {
        if (ctx.IsRemote() && IPAddress.TryParse(ctx.Request.Source.IpAddress, out var ipAddress))
            _remoteClients[ipAddress] = DateTime.UtcNow;

        return Task.FromResult(false);
    }

    // Raised after the primary came back, outside any lock so UI subscribers can dispatch. The
    // hosts reload the SPA in the web view on it; a phone on the remote listener simply retries,
    // so that one is never announced.
    public event EventHandler? Restarted;

    private readonly VpnHoodApp _app;

    private VpnHoodAppWebServer(VpnHoodApp app, WebServerOptions options)
    {
        _app = app;
        var defaultPort = app.Features.WebUiPort ?? 9090;
        var endPoint = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback, defaultPort);
        Url = options.Url ?? new Uri($"http://{endPoint}");

        // A developer can hold the web view's own listener open to the network, and its API open
        // to any origin, for the life of the process: /remote-access in the debug data, or any
        // debug build. Read once here and applied at the next launch, never written back to the
        // settings and never rebound live — a rebind reloads the SPA under the user. The on-demand
        // counterpart for everyone is StartRemoteAccess.
        _isDeveloperRemoteAccess = app.Features.IsDebugMode || app.HasDebugCommand(DebugCommands.RemoteAccess);
        _primary = new WebServerListener("primary", Url.Port,
            () => CreateWebServer(_isDeveloperRemoteAccess ? IPAddress.Any.ToString() : Url.Host, Url.Port));

        AppUiContext.OnResumed += AppUiContextOnResumed;
        AppUiContext.OnChanged += AppUiContextOnChanged;
    }

    // One real connect per listener on every resume, off the UI thread: iOS suspends the process
    // and can close a socket meanwhile. The host reloads the SPA if the primary restarts.
    private void AppUiContextOnResumed(object? sender, EventArgs e)
    {
        Task.Run(async () => {
            try {
                await RestartIfUnreachable().Vhc();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "SPA web server resume check failed.");
            }
        });
    }

    // The activity or window that owned the pairing screen is gone, so the screen is too.
    private void AppUiContextOnChanged(object? sender, EventArgs e)
    {
        if (AppUiContext.Context == null)
            StopRemoteAccess();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            AppUiContext.OnResumed -= AppUiContextOnResumed;
            AppUiContext.OnChanged -= AppUiContextOnChanged;
            // Under the lock so a watchdog tick that already fired can't restart a stopped listener.
            lock (_lock) {
                _disposed = true;
                _watchdogTimer?.Dispose();
                _watchdogTimer = null;
                _remote?.Dispose();
                _remote = null;
                _primary.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    public static VpnHoodAppWebServer Init(VpnHoodApp app, WebServerOptions? options = null)
    {
        var ret = new VpnHoodAppWebServer(app, options ?? new WebServerOptions());
        ret._primary.Start();
        VhLogger.Instance.LogInformation("Web server has been started on {Url}", ret.Url);
        ret._watchdogTimer = new Timer(_ => ret.RestartIfDown(), null, WatchdogInterval, WatchdogInterval);
        return ret;
    }

    // The listeners alive right now. A listener that was released is not in it, which is what
    // keeps recovery from reviving remote access after the screen let go of it.
    private IReadOnlyList<WebServerListener> GetListeners()
    {
        lock (_lock) {
            return _remote != null ? [_primary, _remote] : [_primary];
        }
    }

    // Watchdog: put back any listener that has died. Notify the host so assets interrupted by the
    // outage are loaded again, even when the main document had already finished loading.
    private void RestartIfDown()
    {
        try {
            foreach (var listener in GetListeners())
                if (listener.RestartIfDown() && listener == _primary)
                    OnPrimaryRestarted();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "SPA web server watchdog failed.");
        }
    }

    // Only on concrete signals (resume, a web view that failed to connect), never periodically.
    public async Task RestartIfUnreachable()
    {
        foreach (var listener in GetListeners())
            if (await listener.RestartIfUnreachable().Vhc() && listener == _primary)
                OnPrimaryRestarted();
    }

    // The hosts reload the SPA on Restarted, and that takes the pairing screen with it: no screen,
    // no listener. Raised outside any lock so UI subscribers can dispatch.
    private void OnPrimaryRestarted()
    {
        StopRemoteAccess();
        Restarted?.Invoke(this, EventArgs.Empty);
    }

    // Remote access lives exactly as long as the caller keeps it: the pairing screen starts it when
    // it opens and stops it when it closes, and the UI context going away stops it too. It is a
    // second listener rather than a rebind of the first, so the web view sitting on Url is never
    // reloaded out from under the screen that asked for this. Nothing is persisted; an app restart
    // comes up loopback-only. A repeat call keeps the listener and refreshes the addresses, since a
    // network can change under an open screen. While held it recovers like the primary does.
    //
    // For a developer (debug build or /remote-access) the primary is already on every interface for
    // the life of the process, so the screen gets the primary's own addresses, no second listener is
    // made, and closing the screen changes nothing. The SPA derives the same fact from
    // features.isDebugMode and the command, so it knows not to ask the user to keep the screen open.
    public async Task<RemoteAccessState> StartRemoteAccess()
    {
        // The QR code encodes the first; the rest are the plain-text fallback under it. Our own
        // adapter is "VpnHood.<name>" on Windows (WinTunVpnAdapter); the toolkit already skips tun*.
        var addresses = await IPAddressUtil.GetLanAddresses(AddressFamily.InterNetwork,
            [.. IPAddressUtil.VirtualAdapterMarkers, "VpnHood"]).Vhc();

        lock (_lock) {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_isDeveloperRemoteAccess && _remote == null) {
                // The port it had before, so a rebind keeps an address already on screen valid;
                // otherwise beside the web view's port when that is a user port, so a typed address
                // stays memorable. Below 1024 a bind needs privileges the app does not have.
                var preferredPort = _remotePort != 0 ? _remotePort : Url.Port >= 1024 ? Url.Port + 1 : 0;
                var port = VhUtils.GetFreeTcpEndPoint(IPAddress.Any, preferredPort).Port;
                var remote = new WebServerListener("remote-access", port,
                    () => CreateWebServer(IPAddress.Any.ToString(), port));
                remote.Start();
                _remote = remote;
                _remotePort = port;
            }

            var reachablePort = _isDeveloperRemoteAccess ? Url.Port : _remotePort;
            _remoteAccessUrls = [.. addresses.Select(x => new Uri($"http://{new IPEndPoint(x, reachablePort)}/"))];
            return RemoteAccessState;
        }
    }

    public void StopRemoteAccess()
    {
        lock (_lock) {
            var remote = _remote;
            if (remote == null)
                return;

            remote.Dispose();
            _remote = null;
            _remoteAccessUrls = [];
        }
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

    private WebserverLite CreateWebServer(string host, int port)
    {
        var spaPath = GetSpaPath();
        _indexHtml = File.ReadAllText(Path.Combine(spaPath, "index.html"));

        var settings = new WebserverSettings(host, port);

        // Watson adds "Access-Control-Allow-Origin: *" and friends to every response that did not
        // set them itself. CorsMiddleware decides per origin, and "no header" is one of its answers.
        foreach (var header in new[] { "Access-Control-Allow-Origin", "Access-Control-Allow-Methods", "Access-Control-Allow-Headers" })
            settings.Headers.DefaultHeaders.Remove(header);

        var server = new WebserverLite(settings, ctx => DefaultRoute(ctx, spaPath));
        server.Routes.PreRouting = NoteRemoteClient;

        // Initialize API routes through controllers - CORS is handled centrally in the route mapper
        server
            .AddRouteMapper(_isDeveloperRemoteAccess)
            .AddController(new AppController(_app, this))
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
        CorsMiddleware.AddCors(context, _isDeveloperRemoteAccess);

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
}
