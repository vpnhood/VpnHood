using Foundation;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using VpnHood.AppLib;
using VpnHood.AppLib.Ios.Common;
using VpnHood.AppLib.Services.Ads;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Devices.Ios;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.App.Client.Ios;

[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        if (!VpnHoodApp.IsInit) {
            // The App process has a readable stdout, so a console logger is fine here.
            VhLogger.Instance = VhLogger.CreateConsoleLogger();

            // Load per-product settings the same way the Android Client app does: merge the embedded
            // ".user" appsettings over the in-code defaults (Client is bring-your-own-key, so no default key).
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
        }

        return true;
    }

    private static AppOptions BuildAppOptions(AppConfigs appConfigs)
    {
        var storageFolderPath = AppOptions.BuildStorageFolderPath(AppConfigs.StorageFolderName);

        // Shared client resources bundle the SPA (SpaZipData) served by VpnHoodAppWebServer and
        // shown in the WKWebView. Without SpaZipData the web server cannot start.
        var resources = AppConfigs.Resources;
        resources.Strings.AppName = AppConfigs.AppName;

        return new AppOptions(appId: appConfigs.AppId, storageFolderName: AppConfigs.StorageFolderName,
            isDebugMode: AppConfigs.IsDebugMode) {
            StorageFolderPath = storageFolderPath,
            // Product settings sourced from the embedded ".user" appsettings (parity with Client.Android.Web).
            // Apple applies an additional privacy rule to VPN apps: the iOS build does not send
            // analytics or Firebase reports to third parties. Keep unrelated custom data intact.
            CustomData = WithoutFirebaseOptions(appConfigs.CustomData),
            Ga4MeasurementId = null,
            TrackerFactory = new NullTrackerFactory(),
            AllowEndPointTracker = false,
            RemoteSettingsUrl = appConfigs.RemoteSettingsUrl,
            // Empty until a DefaultAccessKey is supplied (see AppConfigs; Client is bring-your-own-key). An
            // invalid string here would throw inside VpnHoodApp.Init, so we pass an empty array otherwise.
            AccessKeys = string.IsNullOrEmpty(appConfigs.DefaultAccessKey) ? [] : [appConfigs.DefaultAccessKey],
            Resources = resources,
            PrivacyPolicyUrl = appConfigs.PrivacyPolicyUrl,
            // Not appConfigs.TermsOfUseUrl: a purchase here is governed by Apple's standard EULA while
            // no custom EULA is registered in App Store Connect. Delete this line once one is.
            TermsOfUseUrl = new Uri("https://www.apple.com/legal/internet-services/itunes/dev/stdeula/"),
            // The store already took this acceptance at install - see AppOptions.
            IsLicenseAgreementRequired = false,
            // Loopback port for the in-process SPA web server (the WKWebView loads from here).
            WebUiPort = appConfigs.WebUiPort,
            IsAddAccessKeySupported = true,
            // Native in-app rating dialog (parity with Client.Android.Google's Google Play provider).
            // Like Android Client, AllowRecommendUserReviewByServer stays at its default (false).
            UserReviewProvider = new AppStoreInAppUserReviewProvider(),
            // The WKWebView renders edge-to-edge (fills the whole window incl. the status-bar and
            // home-indicator safe areas). false = "don't let the native side pad to the safe area;
            // instead publish the inset sizes (SystemBarsInfo) so the SPA pads itself" — matching the
            // Android clients. With the default (true), SystemBarsInfo is suppressed and the SPA's
            // bottom content slides under the home indicator.
            AdjustForSystemBars = false,
            // State only the exception ForCurrentPlatform cannot see: "Designed for iPad" on Apple
            // Silicon runs the extension without the iOS jetsam cap, yet reports IsIOS() with no
            // Mac Catalyst marker. Only Foundation can tell it from a real device; everything else
            // stays the platform's own choice.
            Transport = NSProcessInfo.ProcessInfo.IsiOSApplicationOnMac
                ? ClientTransportOptions.NormalMemory
                : ClientTransportOptions.ForCurrentPlatform(),
            // Log level: Information in production. To investigate, add the "/log:debug" debug command in
            // the UI (Debug Data 1) — the iOS diagnostics gates are computed from VhLogger.MinLogLevel, so
            // below-Information logging auto-enables them in the extension: vpn-ext.log carries the TcpStack
            // "+CONN/-CONN" and [VHQUIC] +CONN/-CONN/brake lines (EventIds "TcpStack"/"Quic") plus ext-mem.log.
            LogServiceOptions = new LogServiceOptions {
                MinLogLevel = LogLevel.Information
            },
            AdOptions = new AppAdOptions {
                PreloadAd = false
            },
            // Update check via the App Store (parity with Client.Android.Google's Google Play provider):
            // the provider looks up the released store version by bundle id and opens the App Store page
            // when an update is due. UpdateInfoUrl comes from config, which keeps it null on iOS (see
            // AppConfigs) — the store is the only install channel here.
            UpdaterOptions = new AppUpdaterOptions {
                UpdateInfoUrl = appConfigs.UpdateInfoUrl,
                UpdaterProvider = new AppStoreAppUpdaterProvider()
            }
        };
    }

    private static object? WithoutFirebaseOptions(JsonElement? customData)
    {
        if (customData is not { ValueKind: JsonValueKind.Object })
            return customData?.Clone();

        var result = JsonNode.Parse(customData.Value.GetRawText()) as JsonObject;
        result?.Remove("firebaseOptions");
        return result;
    }
}
