using Microsoft.Extensions.Logging;
using NetworkExtension;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.Ios;

public class IosDevice : IDevice
{
    private readonly NETunnelProviderManager _vpnManager = new();

    private const string VpnServiceConfigFolderKey = "VpnServiceConfigFolder";

    // NEVPNProtocol.ServerAddress is a REQUIRED field (SaveToPreferences fails / the Settings entry is
    // invalid if it is nil), but it is purely cosmetic for a packet tunnel — it is only the text shown
    // in iOS Settings. The IP actually used for routing comes from the core via the adapter's ServerIp,
    // not from here. So we use 192.0.2.1 (RFC 5737 TEST-NET-1, guaranteed non-routable) as a placeholder
    // — the same value the IosVpnAdapter falls back to — instead of a real public IP like 1.1.1.1.
    private const string ServerAddress = "192.0.2.1";

    private readonly string _providerBundleId;
    private readonly string _localizedDescription;

    /// <param name="providerBundleId">
    /// The Network Extension's bundle identifier (the Packet Tunnel Provider appex bundle id).
    /// </param>
    /// <param name="sharedContainerPath">
    /// Pre-computed App Group container path. Pass
    /// <c>NSFileManager.DefaultManager.GetContainerUrl(appGroupId)?.Path</c> from the app delegate
    /// before calling <c>VpnHoodApp.Init</c> so the path is stable for the whole session. This path is
    /// forwarded verbatim to the Extension via ProviderConfiguration — an App-Group container is mounted
    /// at the same absolute path in both processes, so the Extension never needs the App Group id.
    /// If <c>null</c> the constructor falls back to LocalApplicationData (App↔Extension IPC will NOT work).
    /// </param>
    /// <param name="localizedDescription">The VPN configuration name shown in iOS Settings.</param>
    public IosDevice(
        string providerBundleId,
        string? sharedContainerPath = null,
        string localizedDescription = "VpnHood")
    {
        _providerBundleId = providerBundleId;
        _localizedDescription = localizedDescription;

        var containerPath = sharedContainerPath
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        VpnServiceConfigFolder = Path.Combine(containerPath, "vpn-service");
        VhLogger.Instance.LogInformation(
            "IosDevice created. VpnServiceConfigFolder={Folder} (sharedContainerPath={Provided})",
            VpnServiceConfigFolder, sharedContainerPath ?? "<not provided>");
    }

    public string VpnServiceConfigFolder { get; }

    public bool IsBindProcessToVpnSupported => false;
    public bool IsTcpProxySupported => false;
    public bool IsExcludeAppsSupported => false;
    public bool IsIncludeAppsSupported => false;
    public bool IsAlwaysOnSupported => false;
    public string OsInfo { get; } = $"{UIDevice.CurrentDevice.SystemName}: {UIDevice.CurrentDevice.Model}, iOS: {UIDevice.CurrentDevice.SystemVersion}";
    public bool IsTv => false;
    public DeviceMemInfo? MemInfo => null;
    public static bool IsVpnServiceProcess => true; // iOS always runs in the VPN service process.

    public void BindProcessToVpn(bool value)
    {
        if (value)
            throw new NotSupportedException("Binding process to VPN is not supported on iOS.");
    }

    public DeviceAppInfo[] InstalledApps =>
        throw new NotSupportedException("VPN filtering (App Filter) is not supported for regular iOS VPN apps");


    public Task RequestVpnService(IUiContext? uiContext, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return Task.CompletedTask; // not needed for iOS, as VPN service is managed by the system.
    }

    public async Task StartVpnService(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation(
            "StartVpnService: VpnServiceConfigFolder={Folder}",
            VpnServiceConfigFolder);

        // ---- Pre-stop: detect stale tunnel ----
        try {
            await LoadFromPreferencesAsync(_vpnManager);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Pre-stop LoadFromPreferences failed.");
        }

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
                await Task.Delay(500, cancellationToken);
                var s = _vpnManager.Connection.Status;
                if (s is NEVpnStatus.Disconnected or NEVpnStatus.Invalid) break;
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
        // Forward the resolved shared-container path verbatim; the Extension resolves its config folder
        // from this key alone (no App Group id needed — the container path is identical in both processes).
        providerProtocol.ProviderConfiguration = NSDictionary<NSString, NSObject>.FromObjectsAndKeys(
            [(NSString)VpnServiceConfigFolder],
            [(NSString)VpnServiceConfigFolderKey]);

        providerProtocol.ServerAddress = ServerAddress;
        providerProtocol.EnforceRoutes = true;
        providerProtocol.IncludeAllNetworks = false;
        _vpnManager.ProtocolConfiguration = providerProtocol;
        _vpnManager.LocalizedDescription = _localizedDescription;
        _vpnManager.Enabled = true;

        // ---- Save ----
        await SaveToPreferencesAsync(_vpnManager);

        // ---- Load ----
        await LoadFromPreferencesAsync(_vpnManager);

        // ---- StartVpnTunnel (synchronous call into iOS) ----
        // The error MUST be inspected: on reconnect StartVpnTunnel commonly returns
        // NEVPNErrorConfigurationStale (the config was modified since this manager last loaded) or
        // ...Disabled. Silently ignoring it is why the tunnel never comes back after the first
        // connect/disconnect cycle. On failure, re-assert+save+reload the config and retry once.
        if (!TryStartTunnel(out var startError) && startError != null) {
            VhLogger.Instance.LogWarning(
                "StartVpnTunnel failed (code={Code}): {Error}. Re-saving config and retrying...",
                startError.Code, startError.LocalizedDescription);

            _vpnManager.Enabled = true;
            await SaveToPreferencesAsync(_vpnManager);
            await LoadFromPreferencesAsync(_vpnManager);

            if (!TryStartTunnel(out var retryError) && retryError != null)
                throw new Exception($"StartVpnTunnel retry failed (code={retryError.Code}): {retryError.LocalizedDescription}");
        }

        // ---- Poll NEVpnStatus quickly (10 × 200ms = 2s) ----
        for (var i = 0; i < 10; i++) {
            try {
                var status = _vpnManager.Connection.Status;
                if (status is NEVpnStatus.Connected or NEVpnStatus.Connecting or NEVpnStatus.Reasserting) break;
            }
            catch { /* ignore */ }
            await Task.Delay(200, cancellationToken);
        }
    }

    // Returns true if StartVpnTunnel was issued without an immediate error.
    private bool TryStartTunnel(out NSError? error)
    {
        error = null;
        try {
            _vpnManager.Connection.StartVpnTunnel(out error);
            return error == null;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "StartVpnTunnel threw.");
            return false;
        }
    }

    private static Task SaveToPreferencesAsync(NEVpnManager manager)
    {
        var tcs = new TaskCompletionSource();
        manager.SaveToPreferences(nsError => {
            if (nsError != null) tcs.TrySetException(new Exception(nsError.LocalizedDescription));
            else tcs.TrySetResult();
        });
        return tcs.Task;
    }

    private static Task LoadFromPreferencesAsync(NEVpnManager manager)
    {
        var tcs = new TaskCompletionSource();
        manager.LoadFromPreferences(nsError => {
            if (nsError != null) tcs.TrySetException(new Exception(nsError.LocalizedDescription));
            else tcs.TrySetResult();
        });
        return tcs.Task;
    }

    public void Dispose()
    {
    }

    public IMessageClient CreateMessageClient()
    {
        return new IosMessageClient(_vpnManager);
    }
}
