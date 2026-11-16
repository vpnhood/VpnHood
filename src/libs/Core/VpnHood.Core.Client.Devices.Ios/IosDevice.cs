using Microsoft.Extensions.Logging;
using NetworkExtension;
using VpnHood.Core.Client.Devices.Ios.Extensions;
using VpnHood.Core.Client.Devices.Ios.Utils;
using VpnHood.Core.Client.Devices.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Net.Quic.Ios;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.Ios;

/// <summary>
/// Represents an iPhone, iPad or Apple TV as the VPN app sees it: the VPN service runs in the app's Network Extension,
/// its packet tunnel provider, which iOS starts.
/// </summary>
public class IosDevice : IDevice
{
    private NETunnelProviderManager _vpnManager = new();

    private const string VpnServiceConfigFolderKey = "VpnServiceConfigFolder";
    private const string VpnServiceDataFolderKey = "VpnServiceDataFolder";

    /// <summary>
    /// The name of the VPN service's config folder in the shared container, the extension's too (<c>IosVpnService</c>):
    /// <c>vpn-service-config</c>.
    /// </summary>
    public const string VpnServiceConfigFolderName = "vpn-service-config";
    private const string VpnServiceDataFolderName = "vpn-service";

    // NEVPNProtocol.ServerAddress is a REQUIRED field (SaveToPreferences fails / the Settings entry is
    // invalid if it is nil), but it is purely cosmetic for a packet tunnel — it is only the text shown
    // in iOS Settings. The IP actually used for routing comes from the core via the adapter's ServerIp,
    // not from here. So we use 192.0.2.1 (RFC 5737 TEST-NET-1, guaranteed non-routable) as a placeholder
    // — the same value the IosVpnAdapter falls back to — instead of a real public IP like 1.1.1.1.
    private const string ServerAddress = "192.0.2.1";

    private readonly string _providerBundleId;
    private readonly string _localizedDescription;

    /// <summary>
    /// Initializes a new instance of the <see cref="IosDevice"/> class, with the VPN service's folders in the shared
    /// container, the config folder marked to stay out of backups.
    /// </summary>
    /// <param name="providerBundleId">
    /// The Network Extension's bundle identifier (the Packet Tunnel Provider appex bundle id).
    /// </param>
    /// <param name="sharedContainerPath">
    /// The folder of the App Group's container the two processes share: the container itself,
    /// <c>NSFileManager.DefaultManager.GetContainerUrl(appGroupId).Path</c>, or on tvOS its
    /// <c>Library/Caches</c>, the only folder there an app may write. Resolved before <c>VpnHoodApp.Init</c>
    /// so it is stable for the whole session. The folders under it are forwarded verbatim to the Extension
    /// via ProviderConfiguration — an App-Group container is mounted at the same absolute path in both
    /// processes, so the Extension never needs the App Group id. No other folder will do:
    /// LocalApplicationData is a different path in each process.
    /// </param>
    /// <param name="localizedDescription">The VPN configuration name shown in iOS Settings.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="sharedContainerPath"/> or <paramref name="localizedDescription"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="sharedContainerPath"/> or <paramref name="localizedDescription"/> is <see langword="null"/>.
    /// </exception>
    public IosDevice(
        string providerBundleId,
        string sharedContainerPath,
        string localizedDescription)
    {
        ArgumentException.ThrowIfNullOrEmpty(sharedContainerPath);
        ArgumentException.ThrowIfNullOrEmpty(localizedDescription);
        _providerBundleId = providerBundleId;
        _localizedDescription = localizedDescription;

        // Install this process's memory reader as the ambient VhMemory.Instance (if nothing set it yet) so
        // memory-aware consumers can read live figures. Installed here (app) and in IosVpnService (extension)
        // so it works in both processes; each reads its OWN process's footprint.
        IosMemory.Install();

        // the service's own files in a folder of their own, kept out of backups; the proxies stay in the
        // folder they all shared before, which travels
        VpnServiceConfigFolder = Path.Combine(sharedContainerPath, VpnServiceConfigFolderName);
        VpnServiceDataFolder = Path.Combine(sharedContainerPath, VpnServiceDataFolderName);
        IosNoBackupFolder.Create(VpnServiceConfigFolder);
        VhLogger.Instance.LogInformation("IosDevice created. VpnServiceConfigFolder={Folder}", VpnServiceConfigFolder);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="VpnServiceConfigFolderName"/> in the shared container, marked to stay out of backups; a mark that cannot
    /// be set is only logged.
    /// </remarks>
    public string VpnServiceConfigFolder { get; }

    /// <inheritdoc/>
    /// <remarks><c>vpn-service</c> in the shared container, which on tvOS is among the caches.</remarks>
    public string VpnServiceDataFolder { get; }

    internal NETunnelProviderManager VpnManager => _vpnManager;

    /// <inheritdoc/>
    public bool IsBindProcessToVpnSupported => false;

    /// <inheritdoc/>
    public bool IsQuicSupported => IosQuicClient.IsSupported;

    /// <inheritdoc/>
    public bool IsExcludeAppsSupported => false;

    /// <inheritdoc/>
    public bool IsIncludeAppsSupported => false;

    /// <inheritdoc/>
    public bool IsAlwaysOnSupported => false;

    /// <inheritdoc/>
    public IVpnKillSwitch? KillSwitch => null;

    /// <inheritdoc/>
    public string OsInfo { get; } = $"{UIDevice.CurrentDevice.SystemName}: {UIDevice.CurrentDevice.Model}, {(OperatingSystem.IsTvOS() ? "tvOS" : "iOS")}: {UIDevice.CurrentDevice.SystemVersion}";

    /// <inheritdoc/>
    /// <remarks>
    /// As Safari says it, which names no model; an Apple TV, which has no Safari, as its own media player says it.
    /// </remarks>
    public DeviceUserAgentInfo UserAgentInfo { get; } = CreateUserAgentInfo();

    /// <inheritdoc/>
    public bool IsTv => OperatingSystem.IsTvOS();

    /// <inheritdoc/>
    /// <remarks><see cref="RequestVpnService"/> asks nothing here.</remarks>
    public bool IsWaitingForVpnPermission => false;

    /// <inheritdoc/>
    /// <remarks>Not known here: <see langword="null"/>.</remarks>
    public DeviceMemInfo? MemInfo => null;

    internal static bool IsVpnServiceProcess => true; // iOS always runs in the VPN service process.

    // as Safari says it, which names no model: "(iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15
    // (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1", an iPad "iPad; CPU OS 18_0 like Mac OS X";
    // an Apple TV, which has no Safari, as its own media player says it: "Apple TV; CPU OS 18_0 like Mac OS X"
    private static DeviceUserAgentInfo CreateUserAgentInfo()
    {
        var version = UIDevice.CurrentDevice.SystemVersion;
        var osVersion = version.Replace('.', '_');
        return new DeviceUserAgentInfo {
            Platform = UIDevice.CurrentDevice.UserInterfaceIdiom switch {
                UIUserInterfaceIdiom.Pad => $"iPad; CPU OS {osVersion} like Mac OS X",
                UIUserInterfaceIdiom.TV => $"Apple TV; CPU OS {osVersion} like Mac OS X",
                _ => $"iPhone; CPU iPhone OS {osVersion} like Mac OS X"
            },
            Browser = $"AppleWebKit/605.1.15 (KHTML, like Gecko) Version/{version} Mobile/15E148 Safari/604.1"
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <see langword="true"/> throws <see cref="NotSupportedException"/>; <see langword="false"/> does nothing.
    /// </remarks>
    public void BindProcessToVpn(bool value)
    {
        if (value)
            throw new NotSupportedException("Binding process to VPN is not supported on iOS.");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// iOS gives an ordinary VPN app no split by app, per-app VPN being for managed devices, so it always throws
    /// <see cref="NotSupportedException"/>.
    /// </remarks>
    public Task<IReadOnlyList<DeviceAppInfo>> GetInstalledApps(CancellationToken cancellationToken) =>
        throw new NotSupportedException("VPN filtering (App Filter) is not supported for regular iOS VPN apps");

    /// <inheritdoc/>
    /// <remarks>
    /// iOS needs nothing here: it completes at once, and iOS asks the person, the first time, when the start saves the
    /// VPN configuration.
    /// </remarks>
    public Task RequestVpnService(IUiContext? uiContext, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It stops a tunnel iOS still runs, waiting up to 4 seconds for its end, saves the VPN configuration, which points
    /// the extension at the service's folders, and starts the tunnel, saving the configuration again and retrying once
    /// where the start fails. It returns once iOS reports the tunnel connecting, or after 2 seconds.
    /// </remarks>
    /// <exception cref="Foundation.NSErrorException">iOS could not save the VPN configuration or start the tunnel.</exception>
    public async Task StartVpnService(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation(
            "StartVpnService: VpnServiceConfigFolder={Folder}",
            VpnServiceConfigFolder);

        // ---- Pre-stop: detect stale tunnel ----
        _vpnManager = await LoadVpnManager().Vhc();

        NEVpnStatus initialStatus;
        try {
            initialStatus = _vpnManager.Connection.Status;
        }
        catch {
            initialStatus = NEVpnStatus.Invalid;
        }

        if (initialStatus is NEVpnStatus.Connected or NEVpnStatus.Connecting or NEVpnStatus.Reasserting) {
            try { _vpnManager.Connection.StopVpnTunnel(); }
            catch { /* ignore */ }
            // Wait up to 4s for it to actually disconnect
            for (var i = 0; i < 8; i++) {
                await Task.Delay(500, cancellationToken).Vhc();
                var s = _vpnManager.Connection.Status;
                if (s is NEVpnStatus.Disconnected or NEVpnStatus.Invalid) 
                    break;
            }
        }

        // ---- Delete stale vpn.status ----
        try {
            var statusPath = Path.Combine(VpnServiceConfigFolder, "vpn.status");
            if (File.Exists(statusPath)) File.Delete(statusPath);
        }
        catch { /* ignore */ }

        // ---- Build new protocol ----
        var providerProtocol = new NETunnelProviderProtocol();
        providerProtocol.ProviderBundleIdentifier = _providerBundleId;
        // Forward the resolved shared-container paths verbatim; the Extension resolves its folders from
        // these keys alone (no App Group id needed — the container path is identical in both processes).
        providerProtocol.ProviderConfiguration = NSDictionary<NSString, NSObject>.FromObjectsAndKeys(
            [(NSString)VpnServiceConfigFolder, (NSString)VpnServiceDataFolder],
            [(NSString)VpnServiceConfigFolderKey, (NSString)VpnServiceDataFolderKey]);

        providerProtocol.ServerAddress = ServerAddress;
        providerProtocol.EnforceRoutes = true;
        providerProtocol.IncludeAllNetworks = false;
        _vpnManager.ProtocolConfiguration = providerProtocol;
        _vpnManager.LocalizedDescription = _localizedDescription;
        _vpnManager.Enabled = true;

        // ---- Save ----
        await _vpnManager.SaveToPreferencesAsync().Vhc();

        // ---- Load ----
        await _vpnManager.LoadFromPreferencesAsync().Vhc();

        // ---- StartVpnTunnel (synchronous call into iOS) ----
        // On failure, re-assert+save+reload the config and retry once; a second failure propagates.
        try {
            StartTunnel();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "StartVpnTunnel failed. Re-saving config and retrying...");

            _vpnManager.Enabled = true;
            await _vpnManager.SaveToPreferencesAsync().Vhc();
            await _vpnManager.LoadFromPreferencesAsync().Vhc();

            StartTunnel();
        }

        // ---- Poll NEVpnStatus quickly (10 × 200ms = 2s) ----
        for (var i = 0; i < 10; i++) {
            try {
                var status = _vpnManager.Connection.Status;
                if (status is NEVpnStatus.Connected or NEVpnStatus.Connecting or NEVpnStatus.Reasserting) break;
            }
            catch { /* ignore */ }
            await Task.Delay(200, cancellationToken).Vhc();
        }
    }

    // The configuration saved before, the running one first, as iOS Settings may have started it; a new
    // one only the first time. A new manager saved at each launch is another entry in iOS Settings.
    private static async Task<NETunnelProviderManager> LoadVpnManager()
    {
        var managers = (await NETunnelProviderManager.LoadAllFromPreferencesAsync().Vhc())
            .ToArray<NETunnelProviderManager>()
            .OfType<NETunnelProviderManager>()
            .ToArray();
        var vpnManager =
            managers.FirstOrDefault(x => x.Connection.Status is not (NEVpnStatus.Disconnected or NEVpnStatus.Invalid)) ??
            managers.FirstOrDefault() ??
            new NETunnelProviderManager();

#pragma warning disable CS0618 // Type or member is obsolete
        await MigrateExtraVpnManagers(managers, vpnManager).Vhc();
#pragma warning restore CS0618 // Type or member is obsolete
        return vpnManager;
    }

    // Older apps saved a new manager at each launch, so iOS Settings lists one entry per launch.
    [Obsolete("Migration (2026-10): drop a few months after it ships, with its call in LoadVpnManager.")]
    private static async Task MigrateExtraVpnManagers(IEnumerable<NETunnelProviderManager> managers,
        NETunnelProviderManager vpnManager)
    {
        foreach (var manager in managers.Where(x => !ReferenceEquals(x, vpnManager))) {
            try {
                await manager.RemoveFromPreferencesAsync().Vhc();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogWarning(ex, "Could not remove an extra VPN configuration.");
            }
        }
    }

    // Starts the tunnel and surfaces the synchronous out-NSError as a managed exception.
    // On reconnect StartVpnTunnel commonly reports NEVPNErrorConfigurationStale (the config was
    // modified since this manager last loaded) or ...Disabled; silently ignoring it is why the
    // tunnel never comes back after the first connect/disconnect cycle.
    private void StartTunnel()
    {
        _vpnManager.Connection.StartVpnTunnel(out var error);
        if (error != null)
            throw error.ToException();
    }

    /// <summary>Does nothing: iOS owns the tunnel's lifetime.</summary>
    public void Dispose()
    {
    }

    /// <inheritdoc/>
    /// <remarks>It sends each request to the running tunnel as a provider message.</remarks>
    public IMessageClient CreateMessageClient()
    {
        return new IosMessageClient(this);
    }
}
