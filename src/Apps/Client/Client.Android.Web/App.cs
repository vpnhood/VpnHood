using VpnHood.AppLib.App.Utils;
using Android.Runtime;
using Avalonia.Android;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppUi.Hosting.Avalonia.Android;
using VpnHood.AppLib.App.Android;
using VpnHood.AppLib.App.Android.Constants;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.App.Client.Android.Web;

[Application(
    Label = AppConfigs.AppName,
    Icon = AndroidAppConstants.Icon,
    Banner = AndroidAppConstants.Banner,
    NetworkSecurityConfig = AndroidAppConstants.NetworkSecurityConfig,
    SupportsRtl = AndroidAppConstants.SupportsRtl,
    AllowBackup = AndroidAppConstants.AllowBackup)]
// Avalonia's application base: this head's UI is Avalonia, and Avalonia 12 starts from the
// process's Application (its OnCreate, after the app below). MainActivity shows it.
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<ClassicAvaloniaApp>(javaReference, transfer)
{
    private static AppOptions CreateAppOptions()
    {
        var appConfigs = AppConfigs.Load();

        // The files this build's asset packages placed beside the app, read the way this
        // platform reads them: the IP-location database and the UI's store. The app extracts
        // what it must under its storage - the store once, for the in-process UI and for the
        // web host, which serves the same entries at /assets/ to a paired phone's page.
        var platformAssets = new AndroidAssetProvider(Application.Context);

        var options = new AppOptions(appId: appConfigs.AppId, storageFolderName: "VpnHood", AppConfigs.IsDebugMode) {
            AppName = AppConfigs.AppName,
            CustomData = appConfigs.CustomData,
            PrivacyPolicyUrl = appConfigs.PrivacyPolicyUrl,
            TermsOfUseUrl = appConfigs.TermsOfUseUrl,
            LogoAssetPath = appConfigs.LogoAssetPath,
            PrivacyConsentAssetName = appConfigs.PrivacyConsentAssetName,
            CompanyName = appConfigs.CompanyName,
            AccessKeys = appConfigs.DefaultAccessKey != null ? [appConfigs.DefaultAccessKey] : [],
            IsAddAccessKeySupported = true,
            AdjustForSystemBars = false,
            AllowEndPointStrategy = true,
            AllowRecommendUserReviewByServer = false,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            WebUiPort = appConfigs.WebUiPort,
            RemoteSettingsUrl = appConfigs.RemoteSettingsUrl,
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            UpdaterOptions = new AppUpdaterOptions {
                UpdateInfoUrl = appConfigs.UpdateInfoUrl,
                PromptDelay = TimeSpan.FromDays(1)
            },
            IpLocationZipAsset = new Asset(platformAssets, "iplocations/IpLocations.zip"),
            UiZipAssets = [new Asset(platformAssets, "assets/ui.zip")],
            // the page a paired phone opens: this same UI, as its browser build
            WebRootZipAsset = new Asset(platformAssets, "assets/web-root.zip"),
            WebHostFactory = new VpnHoodAppWebHostFactory()
        };
        return options;
    }

    public override void OnCreate()
    {
        VpnHoodAndroidApp.Init(CreateAppOptions);
        AndroidAvaloniaUi.Init();
        base.OnCreate();
    }

    public override void OnTerminate()
    {
        if (VpnHoodAndroidApp.IsInit)
            VpnHoodAndroidApp.Instance.Dispose();
    }
}