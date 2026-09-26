using VpnHood.AppLib.App.Utils;
using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.App.Connect.Android.Google.FirebaseUtils;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppLib.Api.Accounts;
using VpnHood.AppLib.App;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppUi.Hosting.Avalonia.Android;
using VpnHood.AppLib.Ads.AdMob.Android;
using VpnHood.AppLib.App.Android;
using VpnHood.AppLib.App.Android.Constants;
using VpnHood.AppLib.Stores.GooglePlay;
using VpnHood.AppLib.Portal;
using VpnHood.AppLib.App.Services.Ads;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.App.Connect.Android.Google;

[Application(
    Label = AppConstants.AppName,
    Icon = AndroidAppConstants.Icon,
    Banner = AndroidAppConstants.Banner,
    NetworkSecurityConfig = AndroidAppConstants.NetworkSecurityConfig,
    SupportsRtl = AndroidAppConstants.SupportsRtl,
    Debuggable = AppConstants.IsDebugMode,
    AllowBackup = AndroidAppConstants.AllowBackup)]
// AdMob reads its application id from the manifest; the ad unit ids are settings (ConnectAppConfigs).
[MetaData("com.google.android.gms.ads.APPLICATION_ID", Value = "ca-app-pub-8662231806304184~1740102860")]
// The Avalonia UI's Application: it starts the app from the params below, then the UI, which
// MainActivity shows.
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : AndroidAvaloniaApplication<ClassicAvaloniaApp>(javaReference, transfer)
{
    // Read at run time rather than as the constant, so a Release build's branches are not unreachable code.
    private static bool IsDebug => AppConstants.IsDebugMode;

    // The private DNS providers that do not count as an ad blocker: any other one a person turns on
    // is treated as one (AppAdOptions.RejectAdBlocker).
    private static readonly string[] AllowedPrivateDnsProviders = [
        "one.one.one.one",
        "family.cloudflare-dns.com",
        "adult-filter-dns.cleanbrowsing.org",
        "family-filter-dns.cleanbrowsing.org",
        "family.dot.dns.yandex.net",
        "dns.google",
        "dns.quad9.net",
        "common.dot.dns.yandex.net",
        "unfiltered.adguard-dns.com",
        "dot.sb",
        "dns.sb",
        "anycast.uncensoreddns.org",
        "unicast.uncensoreddns.org",
        "dot.libredns.gr"
    ];

    // Called by the platform only in the app's own process: never in the VPN service's or the tile's.
    protected override AppInitParams CreateInitParams()
    {
        return new AppInitParams {
            AppId = PackageName ?? throw new InvalidOperationException("The app has no package name."),
            StorageFolderName = "VpnHoodConnect", // what every shipped build has used
            AppOptionsFactory = CreateAppOptions
        };
    }

    // The product's options, and Google Play's lines on top.
    private static AppOptions CreateAppOptions(AppOptionsContext context)
    {
        var appConfigs = ConnectAppConfigs.Load(typeof(App).Assembly);
        var options = ConnectAppOptions.Create(context, appConfigs);
        // The store already took this acceptance at install - see AppOptions.
        options.IsLicenseAgreementRequired = false;
        options.UserReviewProvider = new GooglePlayInAppUserReviewProvider();
        options.AccountProvider = CreateAppAccountProvider(appConfigs, context);
        options.AdProviderItems = CreateAppAdProviderItems(appConfigs);
        options.TrackerFactory = IsDebug ? new NullTrackerFactory() : new FirebaseAnalyticsTrackerFactory();
        // Nothing forbids a typed code on this channel (App Review 3.1.1 binds the App Store head
        // only). The outside shop stays off: Play forbids steering a buyer to one, so no operator
        // token may raise a web-purchase link in this build.
        options.Premium = ConnectAppOptions.CreatePremium(allowImportAccessCode: true, isPurchaseUrlSupported: false);
        options.AdOptions = new AppAdOptions {
            PreloadAd = true,
            RejectAdBlocker = true,
            AllowedPrivateDnsProviders = AllowedPrivateDnsProviders
        };
        options.UpdaterOptions = new AppUpdaterOptions {
            UpdateInfoUrl = appConfigs.GetUpdateInfoUrl(AppConstants.PackageTitle, "android"),
            UpdaterProvider = new GooglePlayAppUpdaterProvider()
        };
        return options;
    }

    public override void OnCreate()
    {
        // Init Firebase Analytics as a singleton as soon as possible, but not in the tile process: it
        // never reports anything, and FirebaseInitProvider runs only in the default process, so this
        // call would be the whole Firebase start-up cost inside the tile's executing-service window
        // (Play ANR group "Executing service …QuickLaunchTileService"). The VPN service process keeps
        // it on purpose: this same call is what gives Crashlytics crash reporting there.
        if (!FirebaseAnalyticsTracker.IsInit && !IsDebug && !QuickLaunchTileService.IsTileProcess)
            FirebaseAnalyticsTracker.Init();

        // the app, then the UI
        base.OnCreate();
    }

    // An ad network whose ids the settings do not name is left out, as a build without the private
    // settings leaves out the portal.
    private static AppAdProviderItem[] CreateAppAdProviderItems(ConnectAppConfigs appConfigs)
    {
        var items = new List<AppAdProviderItem>();

        // interstitial
        if (appConfigs.AdMobInterstitialAdUnitId is { } interstitialAdUnitId)
            items.Add(new AppAdProviderItem {
                AdProvider = AdMobInterstitialAdProvider.Create(interstitialAdUnitId),
                ExcludeCountryCodes = ["CN", "RU"],
                ProviderName = "AdMob"
            });

        // rewarded ad
        if (appConfigs.AdMobRewardedAdUnitId is { } rewardedAdUnitId)
            items.Add(new AppAdProviderItem {
                AdProvider = AdMobRewardedAdProvider.Create(rewardedAdUnitId),
                ExcludeCountryCodes = ["CN", "RU"],
                ProviderName = "AdMob-Rewarded"
            });

        items.Add(new AppAdProviderItem {
            AdProvider = new InternalInAdProvider(),
            ProviderName = "InternalAd",
            IsFallback = true
        });


        /*var initializeTimeout = TimeSpan.FromSeconds(5);
        if (InMobiAdProvider.IsAndroidVersionSupported)
            items.Add(new AppAdProviderItem {
                AdProvider = InMobiAdProvider.Create(
                    appConfigs.InmobiAccountId, appConfigs.InmobiPlacementId, initializeTimeout, AppConstants.IsDebugMode),
                ExcludeCountryCodes = ["CN", "RU"],
                ProviderName = "InMobi"
            });*/

        //if (ChartboostAdProvider.IsAndroidVersionSupported)
        //    items.Add(new AppAdProviderItem {
        //        AdProvider = ChartboostAdProvider.Create(appConfigs.ChartboostAppId, appConfigs.ChartboostAppSignature,
        //            appConfigs.ChartboostAdLocation, initializeTimeout),
        //        ExcludeCountryCodes = ["IR", "CN"],
        //        ProviderName = "Chartboost"
        //    });

        return [.. items];
    }

    private static IAccountProvider? CreateAppAccountProvider(ConnectAppConfigs appConfigs, AppOptionsContext context)
    {
        try {
            // no Portal configured — ship without account features rather than half-wired ones
            if (appConfigs.PortalBaseUri == null) {
                VhLogger.Instance.LogWarning("PortalBaseUri is not configured. Account features are disabled.");
                return null;
            }

            var googleSignInClientId = appConfigs.GoogleSignInClientId ??
                                       throw new InvalidOperationException("GoogleSignInClientId is not configured.");
            var authenticationExternalProvider = new GooglePlayAuthenticationProvider(googleSignInClientId);
            var googlePlayBillingProvider = TryCreateBillingClient();

            // a Debug build may reach a development portal's certificate
            var portalAuthenticationProvider = new PortalAuthenticationProvider(context.StoragePath,
                appConfigs.PortalBaseUri, context.AppId, [authenticationExternalProvider],
                restoreCredentialProvider: new GoogleRestoreCredentialProvider(),
                ignoreSslVerification: AppConstants.IsDebugMode);

            // The portal owns the catalog: it maps each store product to the plan that redeems it, so a
            // product it does not map cannot become an entitlement — and cannot be sold here either.
            return new PortalAccountProvider(portalAuthenticationProvider, googlePlayBillingProvider,
                portalBaseUrl: appConfigs.PortalBaseUri, packageName: context.AppId,
                ignoreSslVerification: AppConstants.IsDebugMode);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create AccountService.");
            return null;
        }
    }

    private static IBillingProvider? TryCreateBillingClient()
    {
        try {
            return new GooglePlayBillingProvider();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create GooglePlayBillingProvider.");
            return null;
        }
    }
}