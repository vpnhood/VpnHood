using VpnHood.Core.Toolkit.Net;
using Microsoft.Extensions.Logging;
using VpnHood.App.Client;
using VpnHood.AppLib;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.Device;
using VpnHood.AppLib.Ios.AppStore;
using VpnHood.AppLib.Ios.Common;
using VpnHood.AppLib.Portal;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.Core.Client.Devices.Ios;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.App.Connect.Ios;

[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
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
            var sharedContainerPath = NSFileManager.DefaultManager.GetContainerUrl(AppConfigs.AppGroupId).Path;
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
            // AllowImportAccessCode and IsPurchaseUrlSupported stay at their default (false): App Review
            // 3.1.1 forbids unlocking with a license key — a premium code is one by Apple's reading —
            // and 3.1.3 forbids steering a buyer to an outside shop, so this build ships with neither
            // a code box nor a web-purchase link, whatever an operator's token offers (lifecycle §9).
            // Website purchases arrive via sign-in and the server-chosen code instead.
            Premium = new AppPremiumOptions { Features = ConnectAppResources.PremiumFeatures },
            // Sign in with Apple + StoreKit billing on the Portal backend. Null when PortalBaseUri is
            // absent from the embedded appsettings: the app then runs sign-in-less (fail-soft, the
            // same contract as Connect.Android.Google).
            AccountProvider = CreateAppAccountProvider(appConfigs, storageFolderPath),
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
            // Kernel buffer for EVERY managed TCP socket the client opens — the transport connections
            // to the server AND the per-flow direct sockets of split/exclude ("passthru") flows. The
            // passthru sockets are the sizing constraint: unlike tunneled flows (QUIC streams bounded
            // tunnel-wide by IosQuicClient's 256 KB aggregate window), each excluded flow pins its own
            // socket buffers, up to the TcpStack's 40-connection cap. The former 256 KB let a
            // split-country browse pin ~40 × 512 KB ≈ 20 MB and jetsam the extension; 64 KB bounds the
            // worst case to ~5 MB while still allowing ~25 Mbps per flow at 20 ms RTT (in-country hosts
            // are low-RTT). Desktop/Android are unaffected — this is iOS-only config.
            TcpKernelBufferSize = new TransferBufferSize(64 * 1024, 64 * 1024),
            // Packet mode multiplexes every inner TCP flow over one outer TCP connection on iOS.
            // Keep direct/split-flow sockets at 64 KB, but give the single server tunnel socket enough
            // BDP window to avoid the observed 10-15 Mbps TCP-mode ceiling.
            TcpPacketChannelKernelBufferSize = new TransferBufferSize(256 * 1024, 256 * 1024),
            // Log level: Information in production. To investigate, add the "/log:debug" debug command in
            // the UI (Debug Data 1) — the iOS diagnostics gates are computed from VhLogger.MinLogLevel, so
            // below-Information logging auto-enables them in the extension: vpn-ext.log carries the TcpStack
            // "+CONN/-CONN" and [VHQUIC] +CONN/-CONN/brake lines (EventIds "TcpStack"/"Quic") plus ext-mem.log.
            LogServiceOptions = new LogServiceOptions {
                MinLogLevel = LogLevel.Information
            },
            // Update check via the App Store (parity with Connect.Android.Google's Google Play provider):
            // the provider looks up the released store version by bundle id and opens the App Store page
            // when an update is due. While Connect iOS is TestFlight-only the lookup finds no listing and
            // the check is a no-op; it starts working with the first App Store release. UpdateInfoUrl comes
            // from config, which keeps it null on iOS (see AppConfigs) — the store is the only install
            // channel here.
            UpdaterOptions = new AppUpdaterOptions {
                UpdateInfoUrl = appConfigs.UpdateInfoUrl,
                UpdaterProvider = new AppStoreAppUpdaterProvider()
            }
        };
    }

    // Mirrors Connect.Android.Google's CreateAppAccountProvider, with the Apple pieces swapped in:
    // Sign in with Apple (email scope only — the no-name policy in APP_STORE_PRIVACY.md) as the
    // external identity, StoreKit 2 as the billing provider, the Portal as the account backend.
    private static IAccountProvider? CreateAppAccountProvider(AppConfigs appConfigs, string storageFolderPath)
    {
        try {
            // no Portal configured — ship without account features rather than half-wired ones
            if (appConfigs.PortalBaseUri == null) {
                VhLogger.Instance.LogWarning("PortalBaseUri is not configured. Account features are disabled.");
                return null;
            }

            var appleAuthenticationProvider = new AppleAuthenticationProvider();
            var appStoreBillingProvider = new AppStoreBillingProvider();

            var portalAuthenticationProvider = new PortalAuthenticationProvider(storageFolderPath,
                appConfigs.PortalBaseUri, appConfigs.AppId, [appleAuthenticationProvider],
                ignoreSslVerification: appConfigs.PortalIgnoreSslVerification);

            // The portal owns the catalog: it maps each store product to the plan that redeems it, so a
            // product it does not map cannot become an entitlement — and cannot be sold here either.
            return new PortalAccountProvider(portalAuthenticationProvider, appStoreBillingProvider,
                portalBaseUrl: appConfigs.PortalBaseUri, packageName: appConfigs.AppId,
                ignoreSslVerification: appConfigs.PortalIgnoreSslVerification);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create the account provider.");
            return null;
        }
    }
}
