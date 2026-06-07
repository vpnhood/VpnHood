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

    private readonly string _appGroupId;
    private readonly string _providerBundleId;
    private readonly string _localizedDescription;
    private readonly string _serverAddress;

    // Cached at construction time from the caller-supplied path (most reliable) or GetContainerUrl fallback.

    /// <param name="appGroupId">
    /// The App Group identifier shared by the host app and the Network Extension
    /// (e.g. <c>group.com.example.client</c>). Must match the App Group used by the extension.
    /// </param>
    /// <param name="providerBundleId">
    /// The Network Extension's bundle identifier (the Packet Tunnel Provider appex bundle id).
    /// </param>
    /// <param name="sharedContainerPath">
    /// Pre-computed App Group container path. Pass
    /// <c>NSFileManager.DefaultManager.GetContainerUrl(appGroupId)?.Path</c> from the app delegate
    /// before calling <c>VpnHoodApp.Init</c> so the path is stable for the whole session.
    /// If <c>null</c> the constructor falls back to GetContainerUrl, then to LocalApplicationData.
    /// </param>
    /// <param name="localizedDescription">The VPN configuration name shown in iOS Settings.</param>
    /// <param name="serverAddress">
    /// The informational server address shown to iOS for the tunnel. Must be a valid IP literal.
    /// </param>
    public IosDevice(
        string appGroupId,
        string providerBundleId,
        string? sharedContainerPath = null,
        string localizedDescription = "VpnHood",
        string serverAddress = "1.1.1.1")
    {
        _appGroupId = appGroupId;
        _providerBundleId = providerBundleId;
        _localizedDescription = localizedDescription;
        _serverAddress = serverAddress;

        var containerPath = sharedContainerPath
            ?? NSFileManager.DefaultManager.GetContainerUrl(appGroupId).Path
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
            "StartVpnService: VpnServiceConfigFolder={Folder}, GetContainerUrl now={ContainerUrl}",
            VpnServiceConfigFolder,
            NSFileManager.DefaultManager.GetContainerUrl(_appGroupId).AbsoluteString ?? "<null>");

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
        providerProtocol.ProviderConfiguration = NSDictionary<NSString, NSObject>.FromObjectsAndKeys(
            [(NSString)VpnServiceConfigFolder, (NSString)_appGroupId],
            [(NSString)VpnServiceConfigFolderKey, (NSString)"AppGroupId"]);

        providerProtocol.ServerAddress = _serverAddress;
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
