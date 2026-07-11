using System.Text.Json;
using VpnHood.App.Client;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Utils;

namespace VpnHood.App.Connect.Ios;

// Per-product configuration for the iOS Connect app. Mirrors the Android Connect app's AppConfigs
// (Connect.Android.Web): the overridable product settings implement IRequiredAppConfigs and are populated
// at startup from the private ".user" folder (embedded by the csproj as AppSettings.json + a secret
// access_key_default.txt), while the iOS-only platform constants stay as static members referenced by the
// bootstrap (AppDelegate/SceneDelegate). The Client app (Client.Ios) keeps a parallel copy with its own ids.
internal class AppConfigs : AppConfigsBase<AppConfigs>, IRequiredAppConfigs
{
    // ---- iOS platform constants (referenced statically by the bootstrap; not sourced from settings) ----

    // Display name (UI + on-disk storage folder + NEVPNManager localized description).
    // ReSharper disable once HeuristicUnreachableCode
    public const string AppName = IsDebugMode ? "VpnHood! Connect (DEBUG)" : "VpnHood! Connect";

    // App Group enabled on BOTH bundle ids (App + Extension Entitlements.plist). This is the only IPC
    // channel between the host app and the Network Extension.
    public const string AppGroupId = "group.com.vpnhood.connect.ios";

    // The Network Extension (Packet Tunnel Provider) appex bundle id.
    public const string ProviderBundleId = "com.vpnhood.connect.ios.networkextension";

    // Connect SPA + colors/icons bundle (from the shared VpnHood.App.Client project).
    public static AppResources Resources => ConnectAppResources.Resources;

    // ---- IRequiredAppConfigs: overridable product settings (merged from embedded AppSettings.json) ----

    public string AppId { get; set; } = "com.vpnhood.connect.ios";

    // Loopback port for the in-process SPA web server (the WKWebView loads from here). Distinct from the
    // Client app's port so both can coexist on one device.
    public int? WebUiPort { get; set; } = 9581;

    // iOS updates through the App Store, so there is no GitHub update feed here (unlike Android). The
    // property is required by IRequiredAppConfigs; left null so no in-app updater is wired.
    public Uri? UpdateInfoUrl { get; set; }

    // PRODUCTION default server key. Left null in code; Load() overrides it from the embedded
    // access_key_default.txt secret (sourced from .user/VpnHoodConnect/web/access_key_default_web.txt).
    // A fork without that secret falls back to null and prompts the user for a key in the UI.
    public string? DefaultAccessKey { get; set; }

    public string? Ga4MeasurementId { get; set; }
    public Uri? RemoteSettingsUrl { get; set; }
    public bool AllowEndPointTracker { get; set; }
    public JsonElement? CustomData { get; set; }

    public static AppConfigs Load()
    {
        var appConfigs = new AppConfigs();
        appConfigs.LoadConfig();

        // The default access key is embedded as its own resource (per configuration) so it can be sourced
        // from a GitHub secret. When present it overrides the in-code/json default.
        var accessKey = appConfigs.ReadResourceText("access_key_default.txt");
        if (!string.IsNullOrWhiteSpace(accessKey))
            appConfigs.DefaultAccessKey = accessKey.Trim();

        return appConfigs;
    }

#if DEBUG
    public const bool IsDebugMode = true;
#else
    public const bool IsDebugMode = false;
#endif
}
