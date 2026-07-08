using System.Text.Json;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Utils;
using VpnHood.Core.Client.Abstractions;

namespace VpnHood.App.Client.Ios;

// Per-product configuration for the iOS Client app. Mirrors the Android Client app's AppConfigs
// (Client.Android.Web): the overridable product settings implement IRequiredAppConfigs and are populated at
// startup from the private ".user" folder (embedded by the csproj as AppSettings.json), while the iOS-only
// platform constants stay as static members referenced by the bootstrap (AppDelegate/SceneDelegate). The
// Connect app (Connect.Ios) keeps a parallel copy with its own ids and a built-in default access key.
internal class AppConfigs : AppConfigsBase<AppConfigs>, IRequiredAppConfigs
{
    // ---- iOS platform constants (referenced statically by the bootstrap; not sourced from settings) ----

    // Display name (UI + on-disk storage folder + NEVPNManager localized description).
    // ReSharper disable once HeuristicUnreachableCode
    public const string AppName = IsDebugMode ? "VpnHood! Client (DEBUG)" : "VpnHood! Client";

    // App Group enabled on BOTH bundle ids (App + Extension Entitlements.plist). This is the only IPC
    // channel between the host app and the Network Extension.
    public const string AppGroupId = "group.com.vpnhood.client.ios";

    // The Network Extension (Packet Tunnel Provider) appex bundle id.
    public const string ProviderBundleId = "com.vpnhood.client.ios.networkextension";

    // Client SPA + colors/icons bundle (from the shared VpnHood.App.Client project).
    public static AppResources Resources => ClientAppResources.Resources;

    // ---- IRequiredAppConfigs: overridable product settings (merged from embedded AppSettings.json) ----

    public string AppId { get; set; } = "com.vpnhood.client.ios";

    // Loopback port for the in-process SPA web server (the WKWebView loads from here). Distinct from the
    // Connect app's port so both can coexist on one device.
    public int? WebUiPort { get; set; } = 9580;

    // iOS updates through the App Store, so there is no GitHub update feed here (unlike Android). The
    // property is required by IRequiredAppConfigs; left null so no in-app updater is wired.
    public Uri? UpdateInfoUrl { get; set; }

    // Client is "bring your own key": no built-in production server. Debug builds seed the shared sample key
    // for convenience; Release ships keyless and the user adds a key in the UI (IsAddAccessKeySupported).
    public string? DefaultAccessKey { get; set; } = IsDebug ? ClientOptions.SampleAccessKey : null;

    public string? Ga4MeasurementId { get; set; }
    public Uri? RemoteSettingsUrl { get; set; }
    public bool AllowEndPointTracker { get; set; }
    public JsonElement? CustomData { get; set; }

    public static AppConfigs Load()
    {
        var appConfigs = new AppConfigs();
        appConfigs.LoadConfig();
        return appConfigs;
    }

#if DEBUG
    public const bool IsDebugMode = true;
#else
    public const bool IsDebugMode = false;
#endif
}
