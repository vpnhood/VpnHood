using VpnHood.AppLib.Utils;
using VpnHood.Core.Toolkit.Assets;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.WebHosting;

// Everything the app knows about its web hosts: which two exist, what each is told, when they come up
// by themselves, and what happens when the answer changes under a running app. The app keeps two
// properties that forward here, because a head holds the app and asks it for a host - it does not hold
// this. Null everywhere when the head handed in no factory: such a head has no web UI at all.
//
// Two hosts, one implementation bound differently. Each is built the first time anything asks for it
// and owned from then on. Under a lock, and in named fields rather than the compiler's: the web view
// asks from its own thread while the API answers on another, one host built twice would leave the
// loser bound and unreachable, and Dispose reads the fields - building a host in order to dispose it
// would be absurd on a head that never opened one.
internal class AppWebHostManager(VpnHoodApp app, IAppWebHostFactory? factory, IAssetProvider? webRoot) : IDisposable
{
    private readonly Lock _lock = new();
    private IAppWebHost? _local;
    private IAppWebHost? _remote;
    private bool _remoteIsDeveloperAccess;

    // What each host is told differs, so each says it here rather than the host inferring it - if the
    // local one should stop coming up by itself, this is the line that changes.
    public IAppWebHost? Local {
        get {
            lock (_lock)
                return _local ??= factory?.CreateLocal(new WebHostCreateParams {
                    Api = app.Api,
                    WebRoot = WebRoot,
                    UiAssetProvider = app.UiAssetProvider,
                    WebUiPort = app.Features.WebUiPort,
                    IsAlwaysOn = true, // the app's own UI loads it; nothing ever stops it
                    IsPairingRequired = true // never asked of it: a loopback caller is not a remote one
                });
        }
    }

    public IAppWebHost? Remote {
        get {
            lock (_lock) {
                if (_remote != null)
                    return _remote;

                // remembered, not re-read: what this host was told is what ApplySettings compares
                _remoteIsDeveloperAccess = IsDeveloperRemoteAccess;
                return _remote = factory?.CreateRemote(new WebHostCreateParams {
                    Api = app.Api,
                    WebRoot = WebRoot,
                    UiAssetProvider = app.UiAssetProvider,
                    WebUiPort = app.Features.WebUiPort,
                    IsAlwaysOn = _remoteIsDeveloperAccess, // otherwise a pairing screen holds it
                    IsPairingRequired = !_remoteIsDeveloperAccess
                });
            }
        }
    }

    // The page both hosts serve, which the head chose (AppOptions.WebRootZipAsset). A head that names a web
    // host and no page has forgotten half of it, and says so here rather than on the first request.
    private IAssetProvider WebRoot => webRoot ?? throw new InvalidOperationException(
        $"The head has set {nameof(AppOptions)}.{nameof(AppOptions.WebHostFactory)} but no " +
        $"{nameof(AppOptions.WebRootZipAsset)}: a web host has no page to serve.");

    // The developer's open door: remote access comes up without a screen and asks for no pairing, so a
    // UI developer can reach a device from their own machine. A host keeps what it was told when it was
    // built, so ApplySettings replaces it when this answer changes under a running app.
    private bool IsDeveloperRemoteAccess =>
        app.Features.IsDebugMode || app.HasDebugCommand(DebugCommands.RemoteAccess);

    // An always-on host is meant to be up without anyone asking for it, and on a head that shows a
    // native UI nobody ever will. Both are asked the same question and each answers for itself - which
    // of them is ever always on is the host's business, not this one's.
    public async Task StartAlwaysOn(CancellationToken cancellationToken)
    {
        if (Local?.IsAlwaysOn == true)
            await VhUtils.TryInvokeAsync("Open local listeners due always on",
                () => Local.EnsureStarted(cancellationToken)).Vhc();

        if (Remote?.IsAlwaysOn == true)
            await VhUtils.TryInvokeAsync("Open remote access listeners due always on",
                () => Remote.EnsureStarted(cancellationToken)).Vhc();
    }

    // The remote-access command is typed into the running app's UI, so the door opens - or shuts -
    // there and then, without a restart nobody would guess was needed. The host is not reconfigured but
    // replaced: a device paired under the old rules loses its token and its connection, which is what
    // switching the open door is. Whether the replacement comes up at all is the host's own answer, as
    // at startup: for a door that just shut, this ends with no listener.
    //
    // Called on every settings save and judged against what the standing host was built with rather
    // than against the settings that were last written, since those are the very same object until the
    // first save of the run. The local host is not in it: nothing it is told can change.
    public void ApplySettings()
    {
        lock (_lock) {
            // no host is built here: nothing wanted one yet, and whatever asks first reads the command
            if (_remote == null || _remoteIsDeveloperAccess == IsDeveloperRemoteAccess)
                return;

            _remote.Dispose();
            _remote = null;
        }

        if (Remote?.IsAlwaysOn == true)
            _ = VhUtils.TryInvokeAsync("Open remote access listeners due always on",
                () => Remote.EnsureStarted(CancellationToken.None));
    }

    public void Dispose()
    {
        // the fields, not the properties: disposing must not build a host
        _local?.Dispose();
        _remote?.Dispose();
    }
}
