using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.Api.WebHost.Helpers;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using VpnHood.AppLib.WebHosting;

namespace VpnHood.AppLib.Api.WebHost;

// One host, made twice: the local one binds loopback for the app's own web view, the remote one binds
// every advertised LAN address for a phone that paired. They serve the same UI and the same API and
// differ only in where they bind, whether a pairing is asked, and who ends them - so what they share
// is everything below, and what differs is the flag. The listener state machine itself is
// WebServerListener, once. Nothing is bound, and nothing is unpacked, until EnsureStarted is called.
public class VpnHoodAppWebHost : IAppWebHost
{
    // The pairing: a short token in the QR's address that becomes a cookie on the first hit. Eight
    // characters from an alphabet without 0/O/1/l, since someone may type it from the screen. One per
    // run of the app, like the port: made at the first bind and kept across stop and start, so a phone
    // that paired earlier in this run gets back in without a scan when the screen is opened again (an
    // accidental Back on the TV would otherwise cost a rescan). The screen still decides when anything
    // is reachable at all; only a restart makes a new token, and nothing is persisted, so it never
    // becomes a standing credential for the install.
    private const string PairQueryName = "pair";
    private const string PairCookieName = "vh-pair";
    private const string BearerPrefix = "Bearer ";
    private const string PairAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";
    private const int PairTokenLength = 8;

    private const string AssetsManifestName = "assets-manifest.json";

    // What both listeners want when nothing else is configured.
    private const int DefaultPort = 9090;

    // A file whose name carries a hash of its content: Vite's "name-<8>.ext", the .NET browser build's
    // "name.<10>.ext" (dotnet.native.<hash>.wasm). index.html, main.js and dotnet.js keep their names.
    private static readonly Regex FingerprintRegex = new(@"(-[a-z0-9_-]{8}|\.[a-z0-9]{10})\.[a-z0-9]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // assets/locales/<culture>.json, the one part of the assets folder that is not a file
    private static readonly Regex LocalePathRegex = new(@"^assets/locales/([\w-]+)\.json$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PresenceWindow = TimeSpan.FromSeconds(5);

    private readonly WebHostCreateParams _createParams;
    private readonly WebHostShared _shared;
    private readonly bool _isRemote;

    // Guards the listeners, their port and token, the advertised addresses and _disposed; each
    // listener locks itself.
    private readonly Lock _lock = new();

    // Presence, not sessions: the remote SPA polls every second, so an address seen within the window
    // is a device that is on, and a closed tab ages out. Keyed by address, so two browsers on one
    // phone count once. Fed by every request that passed the pairing.
    private readonly ConcurrentDictionary<IPAddress, DateTime> _clients = new();

    private IReadOnlyList<WebServerListener> _listeners = []; // replaced whole, never mutated

    // What the last EnsureStarted asked for, which is not what _listeners holds: a listener can die or
    // fail to bind. Recovery aims at this, so a listener that is gone is missed even when nothing is
    // left to notice it. Empty until the first EnsureStarted and again after a Stop, which is what
    // keeps the watchdog from reviving a host the screen let go of.
    private IReadOnlyList<IPAddress> _addresses = [];
    private IReadOnlyList<Uri> _urls = [];
    private Timer? _watchdogTimer;
    private int _port;
    private string? _pairToken;
    private bool _disposed;

    // The app's own API object, put on HTTP by the route table: one instance, every transport, so a
    // paired browser and the device's own UI cannot drift apart.
    private VpnHoodApi Api => _createParams.Api;

    // Up for the life of the process whether or not anything asked, and refusing to stop because
    // nothing holds it that could let go. The app decides which host gets it, and this one obeys.
    public bool IsAlwaysOn { get; }

    // Whether a remote caller must carry the pairing token. The app's rule, not this host's: false is
    // the developer's open door, where no screen exists to read a token from. It never applies to the
    // local listener - a loopback request is not remote, so it is never asked.
    private bool IsPairingRequired => _createParams.IsPairingRequired;

    // Only where a page may legitimately come from somewhere else: a developer's dev server talking to
    // a device. A screen-held pairing keeps the Origin check, since the phone is served that page by
    // this very address.
    private bool AllowAnyOrigin => _isRemote && !IsPairingRequired;

    public bool IsActive => IsAlwaysOn || _listeners.Count > 0;

    // Those of the last EnsureStarted; nothing here touches the network.
    public IReadOnlyList<Uri> Urls => _urls;

    public IReadOnlyList<IPAddress> ConnectedDevices {
        get {
            var threshold = DateTime.UtcNow - PresenceWindow;
            foreach (var stale in _clients.Where(x => x.Value < threshold).Select(x => x.Key).ToArray())
                _clients.TryRemove(stale, out _);

            return [.. _clients.Keys];
        }
    }

    // Raised after a listener came back, outside any lock so UI subscribers can dispatch. The web view
    // reloads the UI on it; a phone on a remote listener simply retries.
    public event EventHandler? Restarted;

    internal VpnHoodAppWebHost(WebHostCreateParams createParams, WebHostShared shared, bool isRemote)
    {
        _createParams = createParams;
        _shared = shared;
        _isRemote = isRemote;

        IsAlwaysOn = createParams.IsAlwaysOn;

        AppUiContext.OnResumed += AppUiContextOnResumed;
        if (isRemote)
            AppUiContext.OnChanged += AppUiContextOnChanged;
    }

    // One real connect per listener on every resume, off the UI thread: iOS suspends the process and
    // can close a socket meanwhile.
    private void AppUiContextOnResumed(object? sender, EventArgs e)
    {
        Task.Run(async () => {
            try {
                await RestartIfUnreachable().Vhc();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "The web host's resume check failed.");
            }
        });
    }

    // The activity or window that owned the pairing screen is gone, so the screen is too. Remote only:
    // the local host outlives every UI context, since the next one loads from it.
    private void AppUiContextOnChanged(object? sender, EventArgs e)
    {
        if (AppUiContext.Context == null)
            StopInternal();
    }

    // The address to load, bound and answering by the time it returns. The first call unpacks the UI
    // and binds; a later one is the caller's own "unreachable" signal and costs one real connect. The
    // remote host also follows its addresses, which can move under an open pairing screen.
    public async Task<Uri> EnsureStarted(CancellationToken cancellationToken)
    {
        var addresses = await GetAddresses().Vhc();
        cancellationToken.ThrowIfCancellationRequested();

        bool wasBound;
        lock (_lock) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            wasBound = _listeners.Count > 0;
            _addresses = addresses;
            if (!wasBound || !addresses.ToHashSet().SetEquals(_listeners.Select(x => x.Address)))
                BindListeners(addresses);

            _urls = [.. _listeners.Select(x => BuildUrl(x.Address))];
        }

        StartWatchdog();
        if (wasBound)
            await RestartIfUnreachable().Vhc();

        return _urls[0];
    }

    // The local host's address never moves, so it is read once. The remote one re-reads every time: a
    // network can change under an open screen. Never 0.0.0.0 - the printed address is the contract, and
    // the VPN's own tunnel or a phone's cellular interface must not carry a control page. Our own
    // adapter is "VpnHood.<name>" on Windows (WinTunVpnAdapter); the toolkit already skips tun*.
    private async Task<IReadOnlyList<IPAddress>> GetAddresses()
    {
        return _isRemote
            ? await IPAddressUtil.GetLanAddresses(AddressFamily.InterNetwork,
                [.. IPAddressUtil.VirtualAdapterMarkers, "VpnHood"]).Vhc()
            : [IPAddress.Loopback];
    }

    // Keeps a listener whose address is still advertised, drops the ones that are not, adds the rest. A
    // phone paired before its address moved stays paired: the token and the port are the host's, not
    // the listener's. Under the lock.
    private void BindListeners(IReadOnlyList<IPAddress> addresses)
    {
        _port = ResolvePort();
        if (_isRemote && IsPairingRequired)
            _pairToken ??= CreatePairToken();

        var kept = _listeners.Where(x => addresses.Contains(x.Address)).ToList();
        foreach (var listener in _listeners.Except(kept))
            listener.Dispose();

        var wanted = addresses.Where(x => kept.All(y => !y.Address.Equals(x))).ToArray();
        var added = TryBind(wanted, _port, out var lastError);

        // The configured port is a contract, but a port nothing can take is worse than one a developer
        // has to look up: a TV that can print no address pairs with nobody. So when it is the whole
        // remote side that could not come up, take a free port instead and keep the feature working.
        // Only then - a listener already serving on the pinned port must not be moved under a screen
        // that is showing its address.
        if (_isRemote && kept.Count == 0 && added.Count == 0 && wanted.Length > 0) {
            _port = VhUtils.GetFreeTcpEndPoint(wanted[0]).Port;
            VhLogger.Instance.LogWarning("Remote access could not take port {ConfiguredPort}; using {Port} instead.",
                _createParams.WebUiPort ?? DefaultPort, _port);
            added = TryBind(wanted, _port, out lastError);
        }

        _listeners = [.. kept, .. added];
        if (_listeners.Count == 0)
            throw new InvalidOperationException("The web host could not be bound on any address.", lastError);
    }

    // An address that vanished between the enumeration and the bind is logged and skipped; the others
    // still serve. None at all is the caller's problem, not this one's.
    private List<WebServerListener> TryBind(IReadOnlyList<IPAddress> addresses, int port, out Exception? lastError)
    {
        lastError = null;
        var bound = new List<WebServerListener>();
        foreach (var address in addresses) {
            var listener = new WebServerListener(_isRemote ? "remote" : "local", address, port,
                () => CreateWebServer(address.ToString(), port));
            try {
                listener.Start();
                bound.Add(listener);
            }
            catch (Exception ex) {
                lastError = ex;
                VhLogger.Instance.LogWarning(ex, "Could not bind the web host on {EndPoint}.", new IPEndPoint(address, port));
                listener.Dispose();
            }
        }

        return bound;
    }

    // The port to bind on now. The local listener is nobody's contract - its address reaches the web
    // view through EnsureStarted - so it asks again on every bind and takes the configured port when it
    // is free, whatever the OS gives when it is not. The remote one is the opposite: the configured
    // port is what a UI developer's dev-server config names, so it is chosen once and then never moves,
    // and a screen showing an address keeps it. BindListeners is the only thing that may overrule that,
    // and only when no address at all could take it.
    private int ResolvePort()
    {
        var configuredPort = _createParams.WebUiPort ?? DefaultPort;
        if (!_isRemote)
            return VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback, configuredPort).Port;

        return _port == 0 ? configuredPort : _port;
    }

    // The web view caches by URL, so the web root's hash rides along on the local address and a build
    // it cached earlier is never served again. A phone gets the pairing token instead, once, from the
    // QR; a developer's listeners ask for none.
    private Uri BuildUrl(IPAddress address)
    {
        var endPoint = new IPEndPoint(address, _port);
        var query = _isRemote
            ? IsPairingRequired ? $"?{PairQueryName}={_pairToken}" : ""
            : $"?nocache={_shared.WebRoot.Hash}";

        return new Uri($"http://{endPoint}/{query}");
    }

    private static string CreatePairToken()
    {
        return new string(RandomNumberGenerator.GetItems<char>(PairAlphabet, PairTokenLength));
    }

    // Recovery for whichever listeners this host has. Armed at the first EnsureStarted and never
    // disarmed: a host that was stopped has no listeners, so a tick does nothing.
    private void StartWatchdog()
    {
        lock (_lock) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _watchdogTimer ??= new Timer(_ => RestartIfDown(), null, WatchdogInterval, WatchdogInterval);
        }
    }

    // The listeners alive right now. One that was released is not in it, which is what keeps recovery
    // from reviving a host the screen let go of.
    private IReadOnlyList<WebServerListener> GetListeners()
    {
        lock (_lock) {
            return _listeners;
        }
    }

    // The one recovery path. A listener that is gone is dropped and bound again through
    // BindListeners, never restarted where it stood: whatever took it down may have taken its port
    // with it, and BindListeners is the only thing allowed to pick another one - it is also what
    // retries an address that failed to bind last time, since _addresses says what should be up.
    // Returns whether anything was rebound. Under the lock.
    private bool RebindListeners(IReadOnlyList<WebServerListener> lost)
    {
        if (_disposed || _addresses.Count == 0)
            return false;

        // the signal may have judged listeners this host has already replaced or let go of
        var dead = _listeners.Where(lost.Contains).ToArray();
        if (dead.Length == 0 && _listeners.Count == _addresses.Count)
            return false;

        VhLogger.Instance.LogWarning(
            "The {Name} web host is short of listeners; binding again. Dead: {Dead}, Alive: {Alive}, Wanted: {Wanted}",
            _isRemote ? "remote" : "local", dead.Length, _listeners.Count, _addresses.Count);

        _listeners = [.. _listeners.Except(dead)];
        foreach (var listener in dead)
            listener.Dispose();

        BindListeners(_addresses);
        _urls = [.. _listeners.Select(x => BuildUrl(x.Address))];
        return true;
    }

    // Watchdog: each listener's own state, which costs no network. Tell the caller, so assets
    // interrupted by the outage are loaded again even when the main document had already finished
    // loading - and so a web view asks for the address again, which is how it follows a moved port.
    private void RestartIfDown()
    {
        try {
            bool restarted;
            lock (_lock)
                restarted = RebindListeners([.. _listeners.Where(x => !x.IsListening)]);

            if (restarted)
                Restarted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The web host's watchdog failed.");
        }
    }

    // Only on concrete signals (a resume, a web view that failed to connect), never periodically. The
    // probes await, so they run outside the lock and the listeners they judged are matched under it:
    // two overlapping signals rebind once, not twice.
    private async Task RestartIfUnreachable()
    {
        var unreachable = new List<WebServerListener>();
        foreach (var listener in GetListeners())
            if (!await listener.IsReachable().Vhc())
                unreachable.Add(listener);

        if (unreachable.Count == 0)
            return;

        bool restarted;
        lock (_lock)
            restarted = RebindListeners(unreachable);

        if (restarted)
            Restarted?.Invoke(this, EventArgs.Empty);
    }

    // A remote caller stopping this is cutting its own line, and that is deliberate: it is the "unpair
    // this device" button, and the phone finding the connection gone is the confirmation. A stopped
    // host is not revived by a poll - the caller checks IsActive first - only by an explicit start.
    public Task Stop(CancellationToken cancellationToken)
    {
        _ = cancellationToken; // dropping listeners is synchronous and cannot be abandoned half-way
        StopInternal();
        return Task.CompletedTask;
    }

    // No early-out on an empty listener list: a host whose last rebind bound nothing still wants its
    // addresses, and it is exactly that intent Stop has to take away. Clearing _addresses is what ends
    // recovery; disposing an empty list costs nothing.
    private void StopInternal()
    {
        lock (_lock) {
            if (IsAlwaysOn)
                return;

            foreach (var listener in _listeners)
                listener.Dispose();

            _listeners = [];
            _addresses = [];
            _urls = [];
        }
    }

    public void Dispose()
    {
        AppUiContext.OnResumed -= AppUiContextOnResumed;
        AppUiContext.OnChanged -= AppUiContextOnChanged;

        // Under the lock so a watchdog tick that already fired can't restart a stopped listener.
        lock (_lock) {
            if (_disposed)
                return;

            _disposed = true;
            _watchdogTimer?.Dispose();
            _watchdogTimer = null;
            foreach (var listener in _listeners)
                listener.Dispose();

            _listeners = [];
            _addresses = [];
            _urls = [];
        }
    }

    // Watson's pre-routing hook, on every listener. Two checks stand in front of every request, the
    // app's own web view included, because CORS governs READING a reply and not sending one: a page on
    // any site can post to an address it guesses, and without these the routes that take their
    // parameters in the query string (connect, disconnect, the intents that open OS settings) would be
    // obeyed while the browser merely hid the answer.
    //
    // 1. Host must name the address this listener bound to. DNS rebinding walks a real browser here
    //    under a stranger's name, and the page then reads replies as same-origin. "localhost" is
    //    allowed on a loopback listener, since the dev server dials it by that name and no one else
    //    can point that name at this machine.
    // 2. An Origin, when it is there, must be one we allow - which includes the pages this server
    //    itself served. A cross-site request always carries its real Origin, so this is the line
    //    between the app's own UI and any other tab.
    //
    // A remote request must also carry the pairing, unless a developer holds the port open. The same
    // token, three ways: in the query once, from the QR, which becomes an HttpOnly cookie for a
    // browser; that cookie afterwards; or a bearer header, for a native client that runs no cookie jar
    // and wants no redirect. The cookie is SameSite=Lax, not Strict: the phone arrives by a navigation
    // from a camera or scanner app, and Strict can be withheld on the redirect that follows, which
    // would hand the hint page to someone who just scanned correctly. Lax still withholds the cookie
    // from a cross-site POST or XHR, and the Origin gate above covers the rest. A header cannot be sent
    // cross-site without a preflight this server refuses, so it needs no such care. Only a request that
    // passed all of it counts as presence, so a scanner is never "connected".
    private async Task<bool> OnPreRouting(HttpContextBase ctx, string boundHost)
    {
        var hostHeader = ctx.Request.RetrieveHeaderValue("Host");
        if (!IsExpectedHost(hostHeader, boundHost)) {
            await ctx.SendPlainText("This address is not one the device is listening on.", (int)HttpStatusCode.Forbidden).Vhc();
            return true;
        }

        var origin = ctx.Request.Headers.Get("Origin");
        if (!string.IsNullOrEmpty(origin) && !CorsMiddleware.IsAllowedOrigin(origin, hostHeader, AllowAnyOrigin)) {
            await ctx.SendPlainText("A page on another site cannot use this API.", (int)HttpStatusCode.Forbidden).Vhc();
            return true;
        }

        if (!ctx.IsRemote())
            return false;

        if (IsPairingRequired) {
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
            _clients[ipAddress] = DateTime.UtcNow;

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

    private WebserverLite CreateWebServer(string host, int port)
    {
        // Unpack before the socket is bound, not on the first request: a listener that answers must
        // have something to answer with, and an unpacking that fails belongs to whoever called
        // EnsureStarted rather than to whichever request happened to arrive first.
        _ = _shared.WebRoot.FolderPath;

        var settings = new WebserverSettings(host, port);

        // Watson adds "Access-Control-Allow-Origin: *" and friends to every response that did not set
        // them itself. CorsMiddleware decides per origin, and "no header" is one of its answers.
        foreach (var header in new[] { "Access-Control-Allow-Origin", "Access-Control-Allow-Methods", "Access-Control-Allow-Headers" })
            settings.Headers.DefaultHeaders.Remove(header);

        var server = new WebserverLite(settings, DefaultRoute);
        server.Routes.PreRouting = ctx => OnPreRouting(ctx, host);

        // Every path of the contract, through its controller - CORS is handled centrally in the route mapper
        server
            .AddRouteMapper(AllowAnyOrigin)
            .AddApi(Api);

        return server;
    }

    private static Task ServeFile(HttpContextBase context, string fullPath, bool isFingerprinted)
    {
        var contentType = MimeTypeUtils.GetContentType(fullPath);
        context.Response.ContentType = contentType;
        // A file whose name carries a hash of its content is a version and can be kept for good; any
        // other name is stable across versions and must be asked for again each time.
        context.Response.Headers["Cache-Control"] = isFingerprinted
            ? "public, max-age=31536000, immutable"
            : "no-cache";
        return context.Response.Send(File.ReadAllBytes(fullPath));
    }

    private static bool IsAssetPath(string localPath)
    {
        return localPath.StartsWith($"assets{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFingerprinted(string localPath)
    {
        return FingerprintRegex.IsMatch(Path.GetFileName(localPath));
    }

    private async Task DefaultRoute(HttpContextBase context)
    {
        // Add CORS centrally for default route
        CorsMiddleware.AddCors(context, AllowAnyOrigin);

        if (context.Request.Url.RawWithoutQuery.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await context.Response.Send();
            return;
        }

        // use LocalPath for security reasons (Url.PathAndQuery can contain double dots)
        var localPath = context.Request.Url.Uri.LocalPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

        // the names under the assets folder, for the one UI that cannot read it: the browser build
        if (string.Equals(localPath, AssetsManifestName, StringComparison.OrdinalIgnoreCase)) {
            await SendAssetsManifest(context);
            return;
        }

        // The words, which the content package carries as resources rather than files: the web UI asks
        // for its locale on the same /assets/ path as everything else (loadLocale in i18n.ts).
        if (LocalePathRegex.Match(localPath.Replace(Path.DirectorySeparatorChar, '/')) is { Success: true } locale) {
            await using var localeStream = Strings.OpenLocaleFile(locale.Groups[1].Value);
            if (localeStream != null) {
                context.Response.ContentType = "application/json";
                context.Response.Headers["Cache-Control"] = "no-cache";
                await context.Response.Send(localeStream.Length, localeStream);
                return;
            }
        }

        // The content package's assets - images, flags, fonts, documents - loaded by name. They are a
        // folder rather than embedded resources because Android's assembly store is per-ABI, and a few
        // hundred files inside a DLL would ship once per architecture. Never fingerprinted.
        if (IsAssetPath(localPath) && AppContent.TryGetFolderPath(out var assetsFolderPath)) {
            var assetPath = Path.GetFullPath(Path.Combine(assetsFolderPath, localPath[(AppContent.FolderName.Length + 1)..]));
            if (assetPath.StartsWith(assetsFolderPath, StringComparison.Ordinal) && File.Exists(assetPath)) {
                await ServeFile(context, assetPath, isFingerprinted: false);
                return;
            }
        }

        // The web root's own file, for the app's web view and a paired device alike; any other path is
        // the UI's to route, and gets index.html.
        var fullPath = Path.Combine(_shared.WebRoot.FolderPath, localPath);
        if (File.Exists(fullPath)) {
            await ServeFile(context, fullPath, IsFingerprinted(localPath));
            return;
        }

        context.Response.ContentType = "text/html";
        await context.Response.Send(_shared.WebRoot.IndexHtml);
    }

    // Only the browser build asks: the assets are a folder (see above), WASM has none, so it copies them
    // into the runtime's own file system at startup - and HTTP gives it no way to enumerate. The list is
    // fixed when the package is built, so it could be generated into the folder and served as a plain
    // file instead, and this route would go.
    private async Task SendAssetsManifest(HttpContextBase context)
    {
        context.Response.Headers["Cache-Control"] = "no-cache";
        await context.SendJson(_shared.AssetNames);
    }
}
