using VpnHood.App.Client;
using VpnHood.AppLib.Abstractions;

namespace VpnHood.App.Connect.Ios;

// Per-product configuration for the iOS Connect app. This is the Connect twin of Client.Ios/AppConfigs.cs;
// the bootstrap (AppDelegate/SceneDelegate/Main) is identical between the two, so all product differences
// live here — mirroring the Android apps' AppConfigs.
internal static class AppConfigs
{
    // Display name (UI + on-disk storage folder + NEVPNManager localized description).
    public const string AppName = "VpnHood! Connect";

    // Host app bundle id.
    public const string AppId = "com.vpnhood.connect.ios";

    // App Group enabled on BOTH bundle ids (App + Extension Entitlements.plist). This is the only IPC
    // channel between the host app and the Network Extension.
    public const string AppGroupId = "group.com.vpnhood.connect.ios";

    // The Network Extension (Packet Tunnel Provider) appex bundle id.
    public const string ProviderBundleId = "com.vpnhood.connect.ios.networkextension";

    // Loopback port for the in-process SPA web server (the WKWebView loads from here). Distinct from the
    // Client app's port so both can coexist on one device without confusion (they are separate sandboxes).
    public const int WebUiPort = 9581;

    // PRODUCTION: no built-in server profile. Supply a key through the UI (IsAddAccessKeySupported) or
    // wire a secret-sourced key here. Do NOT set an invalid placeholder — Token.FromAccessKey runs during
    // VpnHoodApp.Init and throws on a malformed key.
    public const string? DefaultAccessKey = null;

    // Connect SPA + colors/icons bundle (from the shared VpnHood.App.Client project).
    public static AppResources Resources => ConnectAppResources.Resources;
}
