using Microsoft.Extensions.Logging;
using NetworkExtension;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Client.Device.iOS;

public class IosDevice : IDevice
{
    private readonly NETunnelProviderManager _vpnManager = new();

    private const string VpnServiceConfigFolderKey = "VpnServiceConfigFolder";

    private readonly string _appGroupId;
    private readonly string _providerBundleId;
    private readonly string _localizedDescription;
    private readonly string _serverAddress;

    // Cached at construction time from the caller-supplied path (most reliable) or GetContainerUrl fallback.
    private readonly string _vpnServiceConfigFolder;

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
            ?? NSFileManager.DefaultManager.GetContainerUrl(appGroupId)?.Path
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _vpnServiceConfigFolder = Path.Combine(containerPath, "vpn-service");
        VhLogger.Instance.LogInformation(
            "IosDevice created. VpnServiceConfigFolder={Folder} (sharedContainerPath={Provided})",
            _vpnServiceConfigFolder, sharedContainerPath ?? "<not provided>");
    }

    public string VpnServiceConfigFolder => _vpnServiceConfigFolder;

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
            _vpnServiceConfigFolder,
            NSFileManager.DefaultManager.GetContainerUrl(_appGroupId)?.AbsoluteString ?? "<null>");

        // Run an async NE op with a short timeout. On timeout, continue rather than throw
        // (NEVPNManager callbacks may never resume under Mono AOT but the native op still runs).
        static async Task TryStepAsync(Task task, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            var done = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));
            if (done == task) {
                try { await task; }
                catch { /* ignore step failure; subsequent steps continue */ }
            }
        }

        // ---- Pre-stop: detect stale tunnel ----
        var preStopLoad = new TaskCompletionSource();
        _vpnManager.LoadFromPreferences(_ => preStopLoad.TrySetResult());
        await TryStepAsync(preStopLoad.Task, TimeSpan.FromSeconds(2));

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
            var statusPath = Path.Combine(_vpnServiceConfigFolder, "vpn.status");
            if (File.Exists(statusPath)) File.Delete(statusPath);
        }
        catch { /* ignore */ }

        // ---- Build new protocol ----
        var providerProtocol = new NETunnelProviderProtocol();
        providerProtocol.ProviderBundleIdentifier = _providerBundleId;
        providerProtocol.ProviderConfiguration = NSDictionary<NSString, NSObject>.FromObjectsAndKeys(
            new NSObject[] { (NSString)_vpnServiceConfigFolder },
            new NSObject[] { (NSString)VpnServiceConfigFolderKey });
        providerProtocol.ServerAddress = _serverAddress;
        providerProtocol.EnforceRoutes = true;
        providerProtocol.IncludeAllNetworks = false;
        _vpnManager.ProtocolConfiguration = providerProtocol;
        _vpnManager.LocalizedDescription = _localizedDescription;
        _vpnManager.Enabled = true;

        // ---- Save (short timeout; on timeout we continue and try Start anyway) ----
        var saveTcs = new TaskCompletionSource();
        _vpnManager.SaveToPreferences(nsError => {
            if (nsError != null) saveTcs.TrySetException(new Exception(nsError.Description));
            else saveTcs.TrySetResult();
        });
        await TryStepAsync(saveTcs.Task, TimeSpan.FromSeconds(2));

        // ---- Load ----
        var loadTcs = new TaskCompletionSource();
        _vpnManager.LoadFromPreferences(nsError => {
            if (nsError != null) loadTcs.TrySetException(new Exception(nsError.Description));
            else loadTcs.TrySetResult();
        });
        await TryStepAsync(loadTcs.Task, TimeSpan.FromSeconds(2));

        // ---- StartVpnTunnel (synchronous call into iOS) ----
        try {
            _vpnManager.Connection.StartVpnTunnel(out _);
        }
        catch { /* ignore */ }

        // ---- Poll NEVPNStatus quickly (10 × 200ms = 2s) ----
        for (var i = 0; i < 10; i++) {
            try {
                var status = _vpnManager.Connection.Status;
                if (status is NEVpnStatus.Connected or NEVpnStatus.Connecting or NEVpnStatus.Reasserting) break;
            }
            catch { /* ignore */ }
            await Task.Delay(200, cancellationToken);
        }
    }

    public void Dispose()
    {
    }

    public IVpnServiceApiTransport CreateVpnServiceApiTransport()
    {
        return new ProviderMessageVpnServiceApiTransport(_vpnManager);
    }
}
