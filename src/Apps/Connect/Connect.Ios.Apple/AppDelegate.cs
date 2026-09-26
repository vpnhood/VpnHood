using VpnHood.AppUi.Hosting.Avalonia.Ios;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppLib.App.Utils;
using Foundation;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using VpnHood.AppLib.Api.Accounts;
using VpnHood.AppLib.Api.Device;
using VpnHood.AppLib.App;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Stores.AppStore;
using VpnHood.AppLib.App.Ios;
using VpnHood.AppLib.Portal;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.App.Connect.Ios.Apple;

// The Avalonia UI's application delegate: it starts the app from the params below as launching
// finishes, then the UI.
[Register("AppDelegate")]
public class AppDelegate : IosAvaloniaAppDelegate<ClassicAvaloniaApp>
{
    // The folder in the sandbox that every shipped build has kept its settings and saved keys in: the
    // app's name as it was before the name came from the app's identity. iOS paths are case-sensitive
    // and the app's Documents folder outlives updates, so another folder would orphan every install's
    // settings. Frozen; the name may change, this may not.
    // ReSharper disable once HeuristicUnreachableCode
    private const string StorageFolderName = AppConstants.IsDebugMode ? "VpnHood! Connect (DEBUG)" : "VpnHood! Connect";

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
        // The product's settings, as every Connect head loads them, with this head's own built-in key.
        var appConfigs = ConnectAppConfigs.Load(typeof(AppDelegate).Assembly);
        var options = ConnectAppOptions.Create(context, appConfigs);

        // The loopback port of the in-process web host, distinct from the Client app's so both can
        // run on one device.
        options.WebUiPort = 9581;

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
        // Native in-app rating dialog; the product lets the server recommend a review (parity with
        // Connect.Android.Google's Google Play review wiring).
        options.UserReviewProvider = new AppStoreInAppUserReviewProvider();
        // The premium tier stays as the product builds it, with neither a code box nor a web-purchase
        // link: App Review 3.1.1 forbids unlocking with a license key — a premium code is one by
        // Apple's reading — and 3.1.3 forbids steering a buyer to an outside shop, whatever an
        // operator's token offers (lifecycle §9). Website purchases arrive via sign-in and the
        // server-chosen code instead.
        // Sign in with Apple + StoreKit billing on the Portal backend. Null when PortalBaseUri is
        // absent from the embedded appsettings: the app then runs sign-in-less (fail-soft, the
        // same contract as Connect.Android.Google).
        options.AccountProvider = CreateAppAccountProvider(appConfigs, context);
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
        // Update check via the App Store (parity with Connect.Android.Google's Google Play provider):
        // the provider looks up the released store version by bundle id and opens the App Store page
        // when an update is due. While Connect iOS is TestFlight-only the lookup finds no listing and
        // the check is a no-op; it starts working with the first App Store release. No update feed:
        // it describes downloadable packages, so naming one would make the UI offer a direct
        // download — the wrong install channel here, and an App Review problem.
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

    // Mirrors Connect.Android.Google's CreateAppAccountProvider, with the Apple pieces swapped in:
    // Sign in with Apple (email scope only — the no-name policy in APP_STORE_PRIVACY.md) as the
    // external identity, StoreKit 2 as the billing provider, the Portal as the account backend.
    private static IAccountProvider? CreateAppAccountProvider(ConnectAppConfigs appConfigs, AppOptionsContext context)
    {
        try {
            // no Portal configured — ship without account features rather than half-wired ones
            if (appConfigs.PortalBaseUri == null) {
                VhLogger.Instance.LogWarning("PortalBaseUri is not configured. Account features are disabled.");
                return null;
            }

            var appleAuthenticationProvider = new AppleAuthenticationProvider();
            var appStoreBillingProvider = new AppStoreBillingProvider();

            // a Debug build may reach a development portal's certificate
            var portalAuthenticationProvider = new PortalAuthenticationProvider(context.StoragePath,
                appConfigs.PortalBaseUri, context.AppId, [appleAuthenticationProvider],
                ignoreSslVerification: AppConstants.IsDebugMode);

            // The portal owns the catalog: it maps each store product to the plan that redeems it, so a
            // product it does not map cannot become an entitlement — and cannot be sold here either.
            return new PortalAccountProvider(portalAuthenticationProvider, appStoreBillingProvider,
                portalBaseUrl: appConfigs.PortalBaseUri, packageName: context.AppId,
                ignoreSslVerification: AppConstants.IsDebugMode);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create the account provider.");
            return null;
        }
    }
}
