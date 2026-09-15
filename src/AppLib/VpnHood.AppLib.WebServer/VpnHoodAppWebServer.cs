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

// The web view's own listener and the remote-access listeners serve the same SPA and API and differ
// in where they bind and who owns their life. The primary is loopback, alive from Init to Dispose.
// The remote ones are one per advertised LAN address on one shared port, alive from
// StartRemoteAccess to StopRemoteAccess, or for the whole process for a developer. The listener
// state machine itself is WebServerListener, once; all of them get the same recovery from here.
public class VpnHoodAppWebServer : Singleton<VpnHoodAppWebServer>, IDisposable
{
    // The pairing: a short token in the QR's address that becomes a cookie on the first hit. Eight
    // characters from an alphabet without 0/O/1/l, since someone may type it from the screen. One
    // per run of the app, like the port: made at the first start and kept across stop and start,
    // so a phone that paired earlier in this run gets back in without a scan when the screen is
    // opened again (an accidental Back on the TV would otherwise cost a rescan). The screen still
    // decides when anything is reachable at all; only a restart makes a new token, and nothing
    // is persisted, so it never becomes a standing credential for the install.
    private const string PairQueryName = "pair";
    private const string PairCookieName = "vh-pair";
    private const string BearerPrefix = "Bearer ";
    private const string PairAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";
    private const int PairTokenLength = 8;

    private string? _indexHtml;
    private string? _spaHash;
    private string? _spaPath;
    private readonly bool _isDeveloperRemoteAccess;
    private readonly WebServerListener _primary;
    private IReadOnlyList<WebServerListener> _remoteListeners = []; // one per advertised address, replaced whole
    private int _remotePort;
    private string? _pairToken;
    private IReadOnlyList<Uri> _remoteAccessUrls = [];

    // Presence, not sessions: the remote SPA polls every second, so an address seen within the
    // window is a device that is on, and a closed tab ages out. Keyed by address, so two browsers
    // on one phone count once. Fed by every request that passed the pairing, on every listener.
    private readonly ConcurrentDictionary<IPAddress, DateTime> _remoteClients = new();
    private static readonly TimeSpan PresenceWindow = TimeSpan.FromSeconds(5);
    private readonly Lock _lock = new(); // guards the remote listeners, their port and token, and _disposed; each listener locks itself
    private Timer? _watchdogTimer;
    private bool _disposed;
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(5);

    // The address the app's own web view loads: always loopback. Whether other devices can reach
    // the app is a property of the remote listeners, never of this address.
    public Uri Url { get; }

    public string SpaHash => _spaHash ?? throw new InvalidOperationException($"{nameof(SpaHash)} is not initialized");

    // The bundle's assets folder: its images, country flags, fonts, locale files and content
    // documents, each under its own name (the bundle's own code and styles are hashed and live
    // beside it). The SPA loads them from here over this server; a head that shows the native UI
    // instead hands this path to it, so one copy on the device serves both.
    public string AssetsFolderPath => Path.Combine(GetSpaPath(), "assets");
    public bool UseHostName { get; set; }
    public bool IsListening => _primary.IsListening;

    // Held by a pairing screen, or permanent because a developer holds the port open.
    public bool IsRemoteAccessActive => _isDeveloperRemoteAccess || _remoteListeners.Count > 0;

    // Read by the pairing screen while it is open; nothing here touches the network, the
    // addresses are those of the last start or refresh.
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

    // Raised after the primary came back, outside any lock so UI subscribers can dispatch. The
    // hosts reload the SPA in the web view on it; a phone on a remote listener simply retries,
    // so those are never announced.
    public event EventHandler? Restarted;

    private readonly VpnHoodApp _app;

    private VpnHoodAppWebServer(VpnHoodApp app, WebServerOptions options)
    {
        _app = app;
        var defaultPort = app.Features.WebUiPort ?? 9090;
        var endPoint = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback, defaultPort);
        Url = options.Url ?? new Uri($"http://{endPoint}");

        // A developer can hold remote access open for the life of the process, with the API open
        // to any origin and no pairing asked: /remote-access in the debug data, or any debug build.
        // Read once here and applied at the next launch, never written back to the settings. The
        // on-demand counterpart for everyone is StartRemoteAccess.
        _isDeveloperRemoteAccess = app.Features.IsDebugMode || app.HasDebugCommand(DebugCommands.RemoteAccess);
        var primaryAddress = IPAddress.TryParse(Url.Host, out var address) ? address : IPAddress.Loopback;
        _primary = new WebServerListener("primary", primaryAddress, Url.Port, () => CreateWebServer(Url.Host, Url.Port));

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
                foreach (var listener in _remoteListeners)
                    listener.Dispose();
                _remoteListeners = [];
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

        // The developer's listeners come up by themselves. Off this thread: the addresses take a
        // route lookup, and Init is called from the hosts' startup path.
        if (ret._isDeveloperRemoteAccess)
            Task.Run(async () => {
                try {
                    await ret.StartRemoteAccess().Vhc();
                }
                catch (Exception ex) {
                    VhLogger.Instance.LogError(ex, "Could not open the developer's remote access listeners.");
                }
            });

        return ret;
    }

    // The listeners alive right now. A listener that was released is not in it, which is what
    // keeps recovery from reviving remote access after the screen let go of it.
    private IReadOnlyList<WebServerListener> GetListeners()
    {
        lock (_lock) {
            return [_primary, .. _remoteListeners];
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
    // it opens, refreshes it while open, and stops it when it closes; the UI context going away
    // stops it too. It is a set of listeners beside the primary rather than a rebind of it, so the
    // web view sitting on Url is never reloaded out from under the screen that asked for this.
    // One listener per advertised address, never 0.0.0.0: the printed address is the contract, and
    // the VPN's own tunnel or a phone's cellular interface must not carry a control page. Nothing
    // is persisted; an app restart comes up loopback-only. While held they recover like the
    // primary does.
    //
    // For a developer (debug build or /remote-access) the same listeners come up at Init on the
    // primary's own port, stay for the life of the process, and ask for no pairing. The SPA learns
    // that from IsAlwaysOn, so it knows not to ask the user to keep the screen open.
    public Task<RemoteAccessState> StartRemoteAccess()
    {
        return UpdateRemoteAccess(start: true);
    }

    // The pairing screen's poll: a network can change under an open screen, so the address set is
    // read again and the listeners follow it. Starts nothing.
    public Task<RemoteAccessState> RefreshRemoteAccess()
    {
        return UpdateRemoteAccess(start: false);
    }

    private async Task<RemoteAccessState> UpdateRemoteAccess(bool start)
    {
        if (!start && !IsRemoteAccessActive)
            return RemoteAccessState;

        // The QR code encodes the first; the rest are the plain-text fallback under it. Our own
        // adapter is "VpnHood.<name>" on Windows (WinTunVpnAdapter); the toolkit already skips tun*.
        var addresses = await IPAddressUtil.GetLanAddresses(AddressFamily.InterNetwork,
            [.. IPAddressUtil.VirtualAdapterMarkers, "VpnHood"]).Vhc();

        lock (_lock) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!start && _remoteListeners.Count == 0 && !_isDeveloperRemoteAccess)
                return RemoteAccessState; // stopped while the addresses were being read

            if (_remoteListeners.Count == 0 || !addresses.ToHashSet().SetEquals(_remoteListeners.Select(x => x.Address)))
                BindRemoteListeners(addresses);

            var bound = _remoteListeners.Select(x => x.Address).ToHashSet();
            _remoteAccessUrls = [.. addresses.Where(bound.Contains).Select(BuildRemoteUrl)];
            return RemoteAccessState;
        }
    }

    // Keeps a listener whose address is still advertised, drops the ones that are not, adds the
    // rest. A phone paired before its address moved stays paired: the token and the port are the
    // process's, not the listener's. Under the lock.
    private void BindRemoteListeners(IReadOnlyList<IPAddress> addresses)
    {
        // The developer's is the primary's own port. Otherwise beside the web view's port when
        // that is a user port, so a typed address stays memorable; below 1024 a bind needs
        // privileges the app does not have.
        if (_remotePort == 0)
            _remotePort = _isDeveloperRemoteAccess
                ? Url.Port
                : VhUtils.GetFreeTcpEndPoint(IPAddress.Any, Url.Port >= 1024 ? Url.Port + 1 : 0).Port;
        _pairToken ??= CreatePairToken();

        var kept = _remoteListeners.Where(x => addresses.Contains(x.Address)).ToList();
        foreach (var listener in _remoteListeners.Except(kept))
            listener.Dispose();

        // An address that vanished between the enumeration and the bind is logged and skipped; the
        // others still serve. None at all is a failure the caller must see.
        var added = new List<WebServerListener>();
        foreach (var address in addresses.Where(x => kept.All(y => !y.Address.Equals(x)))) {
            var port = _remotePort;
            var listener = new WebServerListener("remote-access", address, port, () => CreateWebServer(address.ToString(), port));
            try {
                listener.Start();
                added.Add(listener);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogWarning(ex, "Could not bind remote access on {EndPoint}.", new IPEndPoint(address, port));
                listener.Dispose();
            }
        }

        _remoteListeners = [.. kept, .. added];
        if (_remoteListeners.Count == 0)
            throw new InvalidOperationException("Remote access could not be bound on any LAN address.");
    }

    private Uri BuildRemoteUrl(IPAddress address)
    {
        var endPoint = new IPEndPoint(address, _remotePort);
        return _isDeveloperRemoteAccess
            ? new Uri($"http://{endPoint}/")
            : new Uri($"http://{endPoint}/?{PairQueryName}={_pairToken}");
    }

    private static string CreatePairToken()
    {
        return new string(RandomNumberGenerator.GetItems<char>(PairAlphabet, PairTokenLength));
    }

    public void StopRemoteAccess()
    {
        lock (_lock) {
            if (_isDeveloperRemoteAccess || _remoteListeners.Count == 0)
                return;

            foreach (var listener in _remoteListeners)
                listener.Dispose();
            _remoteListeners = [];
            _remoteAccessUrls = [];
        }
    }

    // Watson's pre-routing hook, on every listener. Two checks stand in front of every request, the
    // app's own web view included, because CORS governs READING a reply and not sending one: a page
    // on any site can post to an address it guesses, and without these the routes that take their
    // parameters in the query string (connect, disconnect, the intents that open OS settings) would
    // be obeyed while the browser merely hid the answer.
    //
    // 1. Host must name the address this listener bound to. DNS rebinding walks a real browser here
    //    under a stranger's name, and the page then reads replies as same-origin. "localhost" is
    //    allowed on a loopback listener, since the dev server dials it by that name and no one else
    //    can point that name at this machine.
    // 2. An Origin, when it is there, must be one we allow — which includes the pages this server
    //    itself served. A cross-site request always carries its real Origin, so this is the line
    //    between the app's own SPA and any other tab.
    //
    // A remote request must also carry the pairing, unless a developer holds the port open. The
    // same token, three ways: in the query once, from the QR, which becomes an HttpOnly cookie for
    // a browser; that cookie afterwards; or a bearer header, for a native client that runs no cookie
    // jar and wants no redirect. The cookie is SameSite=Lax, not Strict: the phone arrives by a
    // navigation from a camera or scanner app, and Strict can be withheld on the redirect that
    // follows, which would hand the hint page to someone who just scanned correctly. Lax still
    // withholds the cookie from a cross-site POST or XHR, and the Origin gate above covers the rest.
    // A header cannot be sent cross-site without a preflight this server refuses, so it needs no
    // such care. Only a request that passed all of it counts as presence, so a scanner is never
    // "connected".
    private async Task<bool> OnPreRouting(HttpContextBase ctx, string boundHost)
    {
        var hostHeader = ctx.Request.RetrieveHeaderValue("Host");
        if (!IsExpectedHost(hostHeader, boundHost)) {
            await ctx.SendPlainText("This address is not one the device is listening on.", (int)HttpStatusCode.Forbidden).Vhc();
            return true;
        }

        var origin = ctx.Request.Headers.Get("Origin");
        if (!string.IsNullOrEmpty(origin) && !CorsMiddleware.IsAllowedOrigin(origin, hostHeader, _isDeveloperRemoteAccess)) {
            await ctx.SendPlainText("A page on another site cannot use this API.", (int)HttpStatusCode.Forbidden).Vhc();
            return true;
        }

        if (!ctx.IsRemote())
            return false;

        if (!_isDeveloperRemoteAccess) {
            var pairToken = _pairToken;
            if (ctx.Request.QuerystringExists(PairQueryName) && TokenEquals(ctx.Request.RetrieveQueryValue(PairQueryName), pairToken)) {
                ctx.Response.StatusCode = (int)HttpStatusCode.Found;
                ctx.Response.Headers.Add("Set-Cookie", $"{PairCookieName}={pairToken}; Path=/; HttpOnly; SameSite=Lax");
                ctx.Response.Headers.Add("Location", "/");
                await ctx.Response.Send().Vhc();
                return true;
            }

            if (!TokenEquals(ctx.GetCookie(PairCookieName), pairToken) && !TokenEquals(GetBearerToken(ctx), pairToken)) {
                await ctx.SendPlainText("Scan the code on the TV, or type the full address shown under it.", (int)HttpStatusCode.Unauthorized).Vhc();
                return true;
            }
        }

        if (IPAddress.TryParse(ctx.Request.Source.IpAddress, out var ipAddress))
            _remoteClients[ipAddress] = DateTime.UtcNow;

        return false;
    }

    // The name the browser asked for, which is the Host header and nothing else: Watson's own
    // destination fields describe the listener, so they would agree with themselves whatever the
    // browser believed.
    private static bool IsExpectedHost(string? hostHeader, string boundHost)
    {
        var hostname = StripPort(hostHeader);
        if (hostname == null)
            return false;

        if (string.Equals(hostname, boundHost, StringComparison.OrdinalIgnoreCase))
            return true;

        // A loopback listener answers to its name too: the dev server's VITE_API_BASE_URL says
        // localhost, and that name reaches no one else's machine.
        return string.Equals(hostname, "localhost", StringComparison.OrdinalIgnoreCase) &&
               IPAddress.TryParse(boundHost, out var address) && IPAddress.IsLoopback(address);
    }

    private static string? StripPort(string? hostHeader)
    {
        if (string.IsNullOrEmpty(hostHeader))
            return null;

        // IPv6 arrives bracketed, "[::1]:9090", so the port is the colon after the bracket
        var closingBracket = hostHeader.LastIndexOf(']');
        var portSeparator = hostHeader.LastIndexOf(':');
        var hostname = portSeparator > 0 && portSeparator > closingBracket ? hostHeader[..portSeparator] : hostHeader;
        return hostname.Length > 1 && hostname[0] == '[' && hostname[^1] == ']' ? hostname[1..^1] : hostname;
    }

    private static string? GetBearerToken(HttpContextBase ctx)
    {
        var authorization = ctx.Request.Headers.Get("Authorization");
        return authorization != null && authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[BearerPrefix.Length..].Trim()
            : null;
    }

    private static bool TokenEquals(string? presented, string? expected)
    {
        return presented != null && expected != null &&
               CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(presented), System.Text.Encoding.UTF8.GetBytes(expected));
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
        server.Routes.PreRouting = ctx => OnPreRouting(ctx, host);

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
        // The bundle's own files carry a hash of their content in the name, so a name is a version
        // and can be kept for good; the assets folder's names are stable across versions - the
        // native UI reads the same files by name - so its files must be asked for again each time.
        context.Response.Headers["Cache-Control"] = fullPath.Contains($"{Path.DirectorySeparatorChar}assets{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "no-cache"
            : "public, max-age=31536000, immutable";
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
