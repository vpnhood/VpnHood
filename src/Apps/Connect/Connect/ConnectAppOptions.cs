using VpnHood.AppLib.Api.Premium;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.AppLib.App;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.App.Connect;

// What every head of VpnHood Connect says the same way: the product's own word in the UI - its
// logo, consent summary and look - its settings (ConnectAppConfigs), the web host's port,
// the premium tier it sells, and the three files the asset packages place with every head, named
// from the packaged files the platform hands over (AppOptionsContext.PackagedAssetProvider). A head
// adds its channel's lines on top - its updater, its store's billing, sign-in and review, what its
// store permits a buyer - and the platform fills its own defaults after it. An asset this does not
// name is off: nothing fills one in later.
public static class ConnectAppOptions
{
    // One built-in key and no way to add another, so there is no profile to name. A constant, since
    // the desktop commands need it before any app exists (CliHeadParams).
    public const bool IsAddAccessKeySupported = false;

    public static AppOptions Create(AppOptionsContext context, ConnectAppConfigs appConfigs)
    {
        var packagedAssetProvider = context.PackagedAssetProvider;
        // the built-in key, from the build's secret; a Debug build without one starts with the
        // engine's sample
        var defaultAccessKey = appConfigs.DefaultAccessKey ??
                               (AppConstants.IsDebugMode ? ClientOptions.SampleAccessKey : null);
        return new AppOptions(context, AppConstants.IsDebugMode) {
            AppName = AppConstants.AppName,
            PackageTitle = AppConstants.PackageTitle,
            CompanyName = AppConstants.CompanyName,
            LogoAssetPath = "images/logo-connect.png",
            PrivacyConsentAssetName = "privacy-consent-connect",
            PrivacyPolicyUrl = appConfigs.PrivacyPolicyUrl,
            TermsOfUseUrl = appConfigs.TermsOfUseUrl,
            CustomData = appConfigs.CustomData,
            UiTheme = "violet",
            // an empty key would not parse
            AccessKeys = string.IsNullOrEmpty(defaultAccessKey) ? [] : [defaultAccessKey],
            IsAddAccessKeySupported = IsAddAccessKeySupported,
            AllowRecommendUserReviewByServer = true,
            Premium = CreatePremium(allowImportAccessCode: false, isPurchaseUrlSupported: false),
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            RemoteSettingsUrl = appConfigs.RemoteSettingsUrl,
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            AllowEndPointStrategy = appConfigs.AllowEndPointStrategy,
            WebUiPort = AppConstants.IsDebugMode ? 7701 : 7770,
            IpLocationZipAsset = new Asset(packagedAssetProvider, "iplocations/IpLocations.zip"),
            UiZipAssets = [new Asset(packagedAssetProvider, "assets/ui.zip")],
            // the page a paired phone opens: this same UI, as its browser build
            WebRootZipAsset = new Asset(packagedAssetProvider, "assets/web-root.zip"),
            WebHostFactory = new VpnHoodAppWebHostFactory()
        };
    }

    // The tier every Connect build sells, with the two things only a channel may allow a buyer:
    // typing a premium code in, and buying in an outside shop. Create leaves both off, which is what
    // the App Store requires; a head whose channel permits one replaces the tier with this
    // (AppPremiumOptions says why each is off by default).
    public static AppPremiumOptions CreatePremium(bool allowImportAccessCode, bool isPurchaseUrlSupported)
    {
        return new AppPremiumOptions {
            Features = ConnectAppResources.PremiumFeatures,
            AllowImportAccessCode = allowImportAccessCode,
            IsPurchaseUrlSupported = isPurchaseUrlSupported
        };
    }
}
