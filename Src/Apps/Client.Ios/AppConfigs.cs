using VpnHood.App.Client;
using VpnHood.AppLib.Abstractions;

namespace VpnHood.App.Client.Ios;

// Per-product configuration for the iOS Client app. The Connect app (Connect.Ios) keeps a parallel copy
// with its own bundle ids / App Group / resources; the bootstrap (AppDelegate/SceneDelegate/Main) is
// identical between the two, so all product differences live here — mirroring the Android apps' AppConfigs.
internal static class AppConfigs
{
    // Display name (UI + on-disk storage folder + NEVPNManager localized description).
    public const string AppName = "VpnHood! Client";

    // Host app bundle id.
    public const string AppId = "com.vpnhood.client.ios";

    // App Group enabled on BOTH bundle ids (App + Extension Entitlements.plist). This is the only IPC
    // channel between the host app and the Network Extension.
    public const string AppGroupId = "group.com.vpnhood.client.ios";

    // The Network Extension (Packet Tunnel Provider) appex bundle id.
    public const string ProviderBundleId = "com.vpnhood.client.ios.networkextension";

    // Loopback port for the in-process SPA web server (the WKWebView loads from here).
    public const int WebUiPort = 9580;

    // PRODUCTION: no built-in server profile. Supply a key through the UI (IsAddAccessKeySupported) or
    // wire a secret-sourced key here. Do NOT set an invalid placeholder — Token.FromAccessKey runs during
    // VpnHoodApp.Init and throws on a malformed key.
    public const string? DefaultAccessKey = null;

    // Client vs Connect SPA + colors/icons bundle (from the shared VpnHood.App.Client project).
    public static AppResources Resources => ClientAppResources.Resources;
}
