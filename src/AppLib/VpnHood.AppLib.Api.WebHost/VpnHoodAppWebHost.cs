using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.Utils;
using VpnHood.AppLib.Api.WebHost.Helpers;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;

namespace VpnHood.AppLib.Api.WebHost;

// The web view's own listener and the remote-access listeners serve the same SPA and API and differ
// in where they bind and who owns their life. The primary is loopback, alive from Start to Dispose.
// The remote ones are one per advertised LAN address on one shared port, alive from
// StartRemoteAccess to StopRemoteAccess, or for the whole process for a developer. The listener
// state machine itself is WebServerListener, once; all of them get the same recovery from here.
public class VpnHoodAppWebHost : Singleton<VpnHoodAppWebHost>, IRemoteAccessHost, IDisposable
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

    private const string AssetsManifestName = "assets-manifest.json";
    // A file whose name carries a hash of its content: Vite's "name-<8>.ext", the .NET browser build's
    // "name.<10>.ext" (dotnet.native.<hash>.wasm). index.html, main.js and dotnet.js keep their names.
    private static readonly Regex FingerprintRegex = new(@"(-[a-z0-9_-]{8}|\.[a-z0-9]{10})\.[a-z0-9]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ReadOnlyMemory<byte> _webRootZip;
    private string? _indexHtml;
    private string? _webRootHash;
    private string? _webRootPath;
    private string[]? _assetNames;
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

    public string WebRootHash => _webRootHash ?? throw new InvalidOperationException($"{nameof(WebRootHash)} is not initialized");

    // The bundle's assets folder: its images, country flags, fonts, locale files and content
    // documents, each under its own name (the bundle's own code and styles are hashed and live
    // beside it). The SPA loads them from here over this server; a head that shows the native UI
    // instead hands this path to it, so one copy on the device serves both.
    // The folder the UIs load their files from by name, which this server serves at /assets/: the
    // content package's (VpnHood.AppLib.Assets), placed by the app's build.
    public string AssetsFolderPath => AppContent.FolderPath;
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

    // The app's own API object, put on HTTP by the route table: one instance, two transports, so a
    // paired browser and the device's own UI cannot drift apart. Remote access is the one call that
    // comes back here, and it arrives as IRemoteAccessHost - the head wires that up in AppOptions.
    private VpnHoodApi Api => _app.Api;

    private VpnHoodAppWebHost(VpnHoodApp app, WebHostOptions options)
    {
        _app = app;
        _webRootZip = options.WebRootZip;
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
            StopRemoteAccessInternal();
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

    // Construction only, by the head at startup, which has the web root zip. Nothing is extracted or
    // bound until Start, which whoever first needs the address calls - the web view, the Avalonia
    // activity, the pairing screen - so a process that never shows a UI never runs a server.
    public static VpnHoodAppWebHost Init(VpnHoodApp app, WebHostOptions options)
    {
        return new VpnHoodAppWebHost(app, options);
    }

    // Extracts the web root and binds the primary listener. Idempotent.
    public void Start()
    {
        lock (_lock) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_watchdogTimer != null)
                return;

            _primary.Start();
            VhLogger.Instance.LogInformation("Web server has been started on {Url}", Url);
            _watchdogTimer = new Timer(_ => RestartIfDown(), null, WatchdogInterval, WatchdogInterval);
        }

        // The developer's listeners come up by themselves. Off this thread: the addresses take a
        // route lookup, and Start is called from the hosts' startup path.
        if (_isDeveloperRemoteAccess)
            Task.Run(async () => {
                try {
                    await StartRemoteAccess(CancellationToken.None).Vhc();
                }
                catch (Exception ex) {
                    VhLogger.Instance.LogError(ex, "Could not open the developer's remote access listeners.");
                }
            });
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
        StopRemoteAccessInternal();
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
    public Task<RemoteAccessState> StartRemoteAccess(CancellationToken cancellationToken)
    {
        return UpdateRemoteAccess(start: true, cancellationToken);
    }

    // The pairing screen's poll: a network can change under an open screen, so the address set is
    // read again and the listeners follow it. Starts nothing.
    public Task<RemoteAccessState> RefreshRemoteAccess(CancellationToken cancellationToken)
    {
        return UpdateRemoteAccess(start: false, cancellationToken);
    }

    private async Task<RemoteAccessState> UpdateRemoteAccess(bool start, CancellationToken cancellationToken)
    {
        if (!start && !IsRemoteAccessActive)
            return RemoteAccessState;

        // The QR code encodes the first; the rest are the plain-text fallback under it. Our own
        // adapter is "VpnHood.<name>" on Windows (WinTunVpnAdapter); the toolkit already skips tun*.
        cancellationToken.ThrowIfCancellationRequested();
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

    // A remote caller stopping this is cutting its own line, and that is deliberate: it is the
    // "unpair this device" button, and the phone finding the connection gone is the confirmation.
    public Task StopRemoteAccess(CancellationToken cancellationToken)
    {
        _ = cancellationToken; // dropping listeners is synchronous and cannot be abandoned half-way
        StopRemoteAccessInternal();
        return Task.CompletedTask;
    }

    private void StopRemoteAccessInternal()
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

    // The web root under Temp/WebRoot/<hash of its zip>, extracted once per version: a folder whose
    // index.html exists is complete, and an older version's folder goes on the way.
    private string GetWebRootPath()
    {
        if (_webRootPath != null)
            return _webRootPath; // do not extract in same instance

        var hash = Convert.ToHexString(MD5.HashData(_webRootZip.Span));
        var webRootsFolderPath = Path.Combine(_app.StorageFolderPath, "Temp", "WebRoot");
        var webRootPath = Path.Combine(webRootsFolderPath, hash);
        if (!File.Exists(Path.Combine(webRootPath, "index.html"))) {
            if (Directory.Exists(webRootsFolderPath))
                VhUtils.TryInvoke("Delete old WebRoot folder", () => Directory.Delete(webRootsFolderPath, true));
            using var zipArchive = new ZipArchive(OpenZipStream(_webRootZip));
            zipArchive.ExtractToDirectory(webRootPath, true);
        }

        _webRootHash = hash;
        _webRootPath = webRootPath;
        return webRootPath;
    }

    // The zip's own array when it has one; a copy only when it is some other memory.
    private static MemoryStream OpenZipStream(ReadOnlyMemory<byte> zip)
    {
        return MemoryMarshal.TryGetArray(zip, out var segment) && segment.Array != null
            ? new MemoryStream(segment.Array, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(zip.ToArray());
    }

    private WebserverLite CreateWebServer(string host, int port)
    {
        var webRootPath = GetWebRootPath();
        _indexHtml = File.ReadAllText(Path.Combine(webRootPath, "index.html"));

        var settings = new WebserverSettings(host, port);

        // Watson adds "Access-Control-Allow-Origin: *" and friends to every response that did not
        // set them itself. CorsMiddleware decides per origin, and "no header" is one of its answers.
        foreach (var header in new[] { "Access-Control-Allow-Origin", "Access-Control-Allow-Methods", "Access-Control-Allow-Headers" })
            settings.Headers.DefaultHeaders.Remove(header);

        var server = new WebserverLite(settings, ctx => DefaultRoute(ctx, webRootPath));
        server.Routes.PreRouting = ctx => OnPreRouting(ctx, host);

        // Every path of the contract, through its controller - CORS is handled centrally in the route mapper
        server
            .AddRouteMapper(_isDeveloperRemoteAccess)
            .AddApi(Api);

        return server;
    }

    private static Task ServeFile(HttpContextBase context, string fullPath, bool isFingerprinted)
    {
        var contentType = MimeTypeUtils.GetContentType(fullPath);
        context.Response.ContentType = contentType;
        // A file whose name carries a hash of its content is a version and can be kept for good;
        // any other name is stable across versions and must be asked for again each time.
        context.Response.Headers["Cache-Control"] = isFingerprinted
            ? "public, max-age=31536000, immutable"
            : "no-cache";
        return context.Response.Send(File.ReadAllBytes(fullPath));
    }

    // The SPA's own files carry a hash in the name (Vite); the assets folder's names are stable
    // across versions - the native UI reads the same files by name.
    // assets/locales/<culture>.json, the one part of the assets folder that is not a file
    private static readonly Regex LocalePathRegex =
        new(@"^assets/locales/([\w-]+)\.json$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool IsAssetPath(string localPath)
    {
        return localPath.StartsWith($"assets{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFingerprinted(string localPath)
    {
        return FingerprintRegex.IsMatch(Path.GetFileName(localPath));
    }

    private async Task DefaultRoute(HttpContextBase context, string webRootPath)
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

        // the names under the assets folder, for a UI that has to fetch them (the browser build)
        if (string.Equals(localPath, AssetsManifestName, StringComparison.OrdinalIgnoreCase)) {
            await SendAssetsManifest(context);
            return;
        }

        // The words, which the content package carries as resources rather than files: the web UI
        // fetches its own locale file from the same /assets/ path as everything else, and gets it
        // from there (loadLocale in i18n.ts), so the package holds one copy for every UI.
        if (LocalePathRegex.Match(localPath.Replace(Path.DirectorySeparatorChar, '/')) is { Success: true } locale) {
            await using var localeStream = Strings.OpenLocaleFile(locale.Groups[1].Value);
            if (localeStream != null) {
                context.Response.ContentType = "application/json";
                context.Response.Headers["Cache-Control"] = "no-cache";
                await context.Response.Send(localeStream.Length, localeStream);
                return;
            }
        }

        // The assets folder - the images, flags, fonts and documents both UIs load by name - is the
        // content package's, one folder for every UI this server serves. Named by a path a browser
        // caches by, never fingerprinted.
        if (IsAssetPath(localPath) && AppContent.TryGetFolderPath(out var assetsFolderPath)) {
            var assetPath = Path.GetFullPath(Path.Combine(assetsFolderPath, localPath[(AppContent.FolderName.Length + 1)..]));
            if (assetPath.StartsWith(assetsFolderPath, StringComparison.Ordinal) && File.Exists(assetPath)) {
                await ServeFile(context, assetPath, isFingerprinted: false);
                return;
            }
        }

        // The web root's own file, for the app's web view and a paired device alike; any other path
        // is the UI's to route, and gets index.html.
        var fullPath = Path.Combine(webRootPath, localPath);
        if (File.Exists(fullPath)) {
            await ServeFile(context, fullPath, IsFingerprinted(localPath));
            return;
        }

        context.Response.ContentType = "text/html";
        await context.Response.Send(_indexHtml);
    }

    // Every file under the assets folder by its name relative to it, the folder's separators as
    // URL segments: what a UI on the other side of the API fetches before it starts. Read once.
    private async Task SendAssetsManifest(HttpContextBase context)
    {
        _assetNames ??= AppContent.TryGetFolderPath(out var assetsPath)
            ? [
                .. Directory.EnumerateFiles(assetsPath, "*", SearchOption.AllDirectories)
                    .Select(x => Path.GetRelativePath(assetsPath, x).Replace(Path.DirectorySeparatorChar, '/'))
                    .Order(StringComparer.Ordinal)
            ]
            : [];

        context.Response.Headers["Cache-Control"] = "no-cache";
        await context.SendJson(_assetNames);
    }
}
