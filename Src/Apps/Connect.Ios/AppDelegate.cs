using Foundation;
using Microsoft.Extensions.Logging;
using VpnHood.App.Client;
using VpnHood.AppLib;
using VpnHood.AppLib.Ios.Common;
using VpnHood.AppLib.Services.Ads;
using VpnHood.Core.Client.Devices.Ios;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.App.Connect.Ios;

[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    // iOS diagnostics switch (app side) — mirrors the extension-side gates (IosQuicDiagnostics /
    // IosMemoryMonitor), seeded from the VH_IOS_DIAGNOSTICS env var (1/true/yes). Off by default =
    // production. When on, the app tells the extension to log at Debug (via LogServiceOptions below), so
    // the TcpStack "+CONN/-CONN" connection-lifecycle lines and the [VHQUIC] +open/-close/brake events
    // (EventIds "TcpStack"/"Quic") surface in vpn-ext.log instead of being filtered out at Information.
    private static readonly bool DiagnosticsEnabled = ReadDiagnosticsEnv();

    private static bool ReadDiagnosticsEnv()
    {
        try {
            var value = Environment.GetEnvironmentVariable("VH_IOS_DIAGNOSTICS");
            return value is "1" or "true" or "True" or "TRUE" or "yes" or "YES";
        }
        catch {
            return false;
        }
    }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        if (!VpnHoodApp.IsInit) {
            // The App process has a readable stdout, so a console logger is fine here.
            VhLogger.Instance = VhLogger.CreateConsoleLogger();

            // Load per-product settings the same way the Android Connect app does: merge the embedded
            // ".user" appsettings over the in-code defaults and pick up the secret default access key.
            var appConfigs = AppConfigs.Load();

            // Evaluate GetContainerUrl here — on the main thread, after iOS has fully initialized the
            // sandbox — so the App-Group container path (the App<->Extension IPC folder) is stable for
            // the whole session. If this is null the App Group entitlement is missing from the profile.
            var sharedContainerPath = NSFileManager.DefaultManager.GetContainerUrl(AppConfigs.AppGroupId)?.Path;
            VhLogger.Instance.LogInformation(
                "FinishedLaunching: GetContainerUrl({AppGroupId}) = {Path}",
                AppConfigs.AppGroupId, sharedContainerPath ?? "<null>");

            // IosDevice lives in the core VpnHood.Core.Client.Devices.Ios project; it needs the
            // extension's bundle id and the resolved shared-container path to wire up NEVPNManager +
            // the IPC config folder. The App Group id stays here only to compute sharedContainerPath
            // (above) — the Extension receives the resolved path, not the App Group id.
            var device = new IosDevice(
                providerBundleId: AppConfigs.ProviderBundleId,
                sharedContainerPath: sharedContainerPath,
                localizedDescription: AppConfigs.AppName);

            VpnHoodIosApp.Init(device, BuildAppOptions(appConfigs));

            // Keep the iOS Network Extension under the ~52 MB jetsam limit: fewer packet channels
            // (default is 4). Each packet channel is a full TLS connection — native socket + TLS state +
            // a send/receive coalescing buffer pair. Use ONE channel: a single TLS connection comfortably
            // carries ~90 Mbps, so this halves the channel footprint with no real throughput loss and
            // buys headroom so the extension survives the system-memory-pressure spike when the app
            // foregrounds (WKWebView/SPA spin-up) during heavy traffic. Set before the first connect.
            VpnHoodApp.Instance.UserSettings.MaxPacketChannelCount = 1;
            VpnHoodApp.Instance.SettingsService.Save();
        }

        return true;
    }

    private static AppOptions BuildAppOptions(AppConfigs appConfigs)
    {
        var storageFolderPath = AppOptions.BuildStorageFolderPath(AppConfigs.AppName);

        // Shared client resources bundle the SPA (SpaZipData) served by VpnHoodAppWebServer and
        // shown in the WKWebView. Without SpaZipData the web server cannot start.
        var resources = AppConfigs.Resources;
        resources.Strings.AppName = AppConfigs.AppName;

        return new AppOptions(appId: appConfigs.AppId, AppConfigs.AppName, isDebugMode: AppConfigs.IsDebugMode) {
            StorageFolderPath = storageFolderPath,
            // Product settings sourced from the embedded ".user" appsettings (parity with Connect.Android.Web).
            CustomData = appConfigs.CustomData,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            RemoteSettingsUrl = appConfigs.RemoteSettingsUrl,
            // Empty until a DefaultAccessKey is supplied (embedded secret, see AppConfigs.Load). An invalid
            // string here would throw inside VpnHoodApp.Init, so we pass an empty array instead of a placeholder.
            AccessKeys = string.IsNullOrEmpty(appConfigs.DefaultAccessKey) ? [] : [appConfigs.DefaultAccessKey],
            Resources = resources,
            UiName = "VpnHoodConnect",
            // Loopback port for the in-process SPA web server (the WKWebView loads from here).
            WebUiPort = appConfigs.WebUiPort,
            IsAddAccessKeySupported = false,
            PremiumFeatures = ConnectAppResources.PremiumFeatures,
            // The WKWebView renders edge-to-edge (fills the whole window incl. the status-bar and
            // home-indicator safe areas). false = "don't let the native side pad to the safe area;
            // instead publish the inset sizes (SystemBarsInfo) so the SPA pads itself" — matching the
            // Android clients. With the default (true), SystemBarsInfo is suppressed and the SPA's
            // bottom content slides under the home indicator.
            AdjustForSystemBars = false,
            // iOS Network Extension runs under a ~52 MB jetsam limit. Shrink the transport coalescing
            // buffers (iOS-only) to keep memory usage low; these flow to the extension via vpn.config.
            // Desktop/Android keep the 256 KB default. 64 KB holds ~45 MTU packets with negligible
            // throughput impact below ~200 Mbps.
            PacketChannelBufferSize = new TransferBufferSize(16 * 1024, 16 * 1024),
            UdpProxyBufferSize = new TransferBufferSize(16 * 1024, 16 * 1024),
            // UPLOAD/DOWNLOAD SPEED: the proxy copy pump is a serial read→write→flush loop, so per-flow
            // throughput ≈ StreamProxyBufferSize / RTT. 2 KB capped it at ~2 Mbps. 32 KB lifts that ~16×.
            // (Memory is per-ACTIVE-flow: 2 buffers × 32 KB; the many-idle-flows case is bounded separately.)
            StreamProxyBufferSize = new TransferBufferSize(32 * 1024, 32 * 1024),
            // Larger buffer for the TCP kernel/proxy path than StreamProxyBufferSize: keeps throughput
            // near the auto-tuned baseline while the per-burst transient still fits under the ~52 MB
            // jetsam line (device-validated). Desktop/Android are unaffected — this is iOS-only config.
            TcpKernelBufferSize = new TransferBufferSize(256 * 1024, 256 * 1024),
            // Log level: Information in production. The iOS diagnostics switch (DiagnosticsEnabled, seeded
            // from VH_IOS_DIAGNOSTICS) drops it to Debug so the extension's vpn-ext.log carries the
            // TcpStack "+CONN/-CONN" connection-lifecycle lines and the [VHQUIC] +open/-close/brake events
            // (EventIds "TcpStack"/"Quic"). Flows to the extension via ClientOptions.LogServiceOptions.
            LogServiceOptions = new LogServiceOptions {
                MinLogLevel = DiagnosticsEnabled ? LogLevel.Debug : LogLevel.Information
            }
        };
    }
}
