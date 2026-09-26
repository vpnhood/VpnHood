using VpnHood.AppUi.Hosting.Avalonia.Ios;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using Foundation;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Ios;
using VpnHood.AppLib.Stores.AppStore;
using VpnHood.AppLib.App.Services.Ads;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.App.Client.Ios.Apple;

// The Avalonia UI's application delegate: it starts the app from the params below as launching
// finishes, then the UI.
[Register("AppDelegate")]
public class AppDelegate : IosAvaloniaAppDelegate<ClassicAvaloniaApp>
{
    // The folder in the sandbox that every shipped build has kept its settings and saved keys in, apart
    // from the app's name: iOS paths are case-sensitive and the app's Documents folder outlives updates,
    // so another folder would orphan every install's settings. Frozen; the name may change, this may not.
    // ReSharper disable once HeuristicUnreachableCode
    private const string StorageFolderName = AppConstants.IsDebugMode ? "VpnHood! Client (DEBUG)" : "VpnHood! Client";

    // The platform builds the device from the App Group and the extension's bundle id (VpnHoodIosApp).
    protected override IosInitParams CreateInitParams()
    {
        return new IosInitParams {
            // the bundle's own id, which the build took from the app's identity
            AppId = NSBundle.MainBundle.BundleIdentifier ??
                    throw new InvalidOperationException("The app's bundle has no identifier."),
            StorageFolderName = StorageFolderName,
            AppGroupId = AppConstants.AppGroupId,
            ProviderBundleId = AppConstants.ProviderBundleId,
            AppOptionsFactory = BuildAppOptions
        };
    }

    // The product's options, and the App Store's lines on top.
    private static AppOptions BuildAppOptions(AppOptionsContext context)
    {
        // The product's settings, as every Client head loads them (Client is bring-your-own-key,
        // so there is no built-in key).
        var appConfigs = ClientAppConfigs.Load();
        var options = ClientAppOptions.Create(context, appConfigs);

        // The loopback port of the in-process web host, distinct from the Connect app's so both can
        // run on one device.
        options.WebUiPort = 9580;

        // Apple applies an additional privacy rule to VPN apps: the iOS build does not send
        // analytics or Firebase reports to third parties. Keep unrelated custom data intact.
        options.CustomData = WithoutFirebaseOptions(appConfigs.CustomData);
        options.Ga4MeasurementId = null;
        options.TrackerFactory = new NullTrackerFactory();
        options.AllowEndPointTracker = false;
        // Not appConfigs.TermsOfUseUrl: a purchase here is governed by Apple's standard EULA while
        // no custom EULA is registered in App Store Connect. Delete this line once one is.
        options.TermsOfUseUrl = new Uri("https://www.apple.com/legal/internet-services/itunes/dev/stdeula/");
        // The store already took this acceptance at install - see AppOptions.
        options.IsLicenseAgreementRequired = false;
        // Native in-app rating dialog (parity with Client.Android.Google's Google Play provider).
        // Like Android Client, AllowRecommendUserReviewByServer stays at its default (false).
        options.UserReviewProvider = new AppStoreInAppUserReviewProvider();
        // State only the exception ForCurrentPlatform cannot see: "Designed for iPad" on Apple
        // Silicon runs the extension without the iOS jetsam cap, yet reports IsIOS() with no
        // Mac Catalyst marker. Only Foundation can tell it from a real device; everything else
        // stays the platform's own choice.
        options.Transport = NSProcessInfo.ProcessInfo.IsiOSApplicationOnMac
            ? ClientTransportOptions.NormalMemory
            : ClientTransportOptions.ForCurrentPlatform();
        // Log level: Information in production. To investigate, add the "/log:debug" debug command in
        // the UI (Debug Data 1) — the iOS diagnostics gates are computed from VhLogger.MinLogLevel, so
        // below-Information logging auto-enables them in the extension: vpn-ext.log carries the TcpStack
        // "+CONN/-CONN" and [VHQUIC] +CONN/-CONN/brake lines (EventIds "TcpStack"/"Quic") plus ext-mem.log.
        options.LogServiceOptions = new LogServiceOptions {
            MinLogLevel = LogLevel.Information
        };
        options.AdOptions = new AppAdOptions {
            PreloadAd = false
        };
        // Update check via the App Store (parity with Client.Android.Google's Google Play provider):
        // the provider looks up the released store version by bundle id and opens the App Store page
        // when an update is due. No update feed: it describes downloadable packages, so naming one
        // would make the UI offer a direct download — the wrong install channel here, and an App
        // Review problem. The store is the only install channel.
        options.UpdaterOptions = new AppUpdaterOptions {
            UpdaterProvider = new AppStoreAppUpdaterProvider()
        };
        return options;
    }

    private static JsonElement? WithoutFirebaseOptions(JsonElement? customData)
    {
        if (customData is not { ValueKind: JsonValueKind.Object })
            return customData?.Clone();

        var result = JsonNode.Parse(customData.Value.GetRawText()) as JsonObject;
        result?.Remove("firebaseOptions");
        return result is null ? null : JsonSerializer.SerializeToElement(result);
    }
}
