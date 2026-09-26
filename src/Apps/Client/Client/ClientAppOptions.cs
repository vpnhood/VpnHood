using VpnHood.AppLib.Api.WebHost;
using VpnHood.AppLib.App;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.App.Client;

// What every head of VpnHood Client says the same way: the product's own word in the UI - its
// logo and consent summary - its settings (ClientAppConfigs), the web host's port, and the
// three files the asset packages place with every head, named from the packaged files the platform
// hands over (AppOptionsContext.PackagedAssetProvider). A head adds its channel's lines on top - its
// updater, its store's review, what its store requires - and the platform fills its own defaults
// after it. An asset this does not name is off: nothing fills one in later, so "left out" and
// "disabled" cannot be told apart and never need to be.
public static class ClientAppOptions
{
    // The product takes access keys, so its profiles are the person's to manage. A constant, since
    // the desktop commands need it before any app exists (CliHeadParams).
    public const bool IsAddAccessKeySupported = true;

    public static AppOptions Create(AppOptionsContext context, ClientAppConfigs appConfigs)
    {
        var packagedAssetProvider = context.PackagedAssetProvider;
        // bring-your-own-key: none unless the build names one, but a Debug build starts with the
        // engine's sample
        var defaultAccessKey = appConfigs.DefaultAccessKey ??
                               (AppConstants.IsDebugMode ? ClientOptions.SampleAccessKey : null);
        return new AppOptions(context, AppConstants.IsDebugMode) {
            AppName = AppConstants.AppName,
            PackageTitle = AppConstants.PackageTitle,
            CompanyName = AppConstants.CompanyName,
            LogoAssetPath = "images/logo-client.png",
            PrivacyConsentAssetName = "privacy-consent-client",
            PrivacyPolicyUrl = appConfigs.PrivacyPolicyUrl,
            TermsOfUseUrl = appConfigs.TermsOfUseUrl,
            CustomData = appConfigs.CustomData,
            // an empty key would not parse
            AccessKeys = string.IsNullOrEmpty(defaultAccessKey) ? [] : [defaultAccessKey],
            IsAddAccessKeySupported = IsAddAccessKeySupported,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            RemoteSettingsUrl = appConfigs.RemoteSettingsUrl,
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            AllowEndPointStrategy = appConfigs.AllowEndPointStrategy,
            WebUiPort = AppConstants.IsDebugMode ? 4701 : 4700,
            IpLocationZipAsset = new Asset(packagedAssetProvider, "iplocations/IpLocations.zip"),
            UiZipAssets = [new Asset(packagedAssetProvider, "assets/ui.zip")],
            // the page a paired phone opens: this same UI, as its browser build
            WebRootZipAsset = new Asset(packagedAssetProvider, "assets/web-root.zip"),
            WebHostFactory = new VpnHoodAppWebHostFactory()
        };
    }
}
