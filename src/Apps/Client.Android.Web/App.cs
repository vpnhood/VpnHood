using Android.Runtime;
using Avalonia.Android;
using VpnHood.AppLib;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppUi.Hosting.Avalonia.Droid;
using VpnHood.AppLib.Droid.Common;
using VpnHood.AppLib.Droid.Common.Constants;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.AppLib.Api.WebHost;

namespace VpnHood.App.Client.Droid.Web;

[Application(
    Label = AppConfigs.AppName,
    Icon = AndroidAppConstants.Icon,
    Banner = AndroidAppConstants.Banner,
    NetworkSecurityConfig = AndroidAppConstants.NetworkSecurityConfig,
    SupportsRtl = AndroidAppConstants.SupportsRtl,
    AllowBackup = AndroidAppConstants.AllowBackup)]
// Avalonia's application base: the Avalonia UI ships beside the web view, and Avalonia 12 starts
// from the process's Application (its OnCreate, after the app below). AvaloniaActivity shows it,
// when MainActivity hands the launch over (DebugCommands.AvaloniaUi).
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<ClassicAvaloniaApp>(javaReference, transfer)
{
    private static AppOptions CreateAppOptions()
    {
        var appConfigs = AppConfigs.Load();
        var resources = ClientAppResources.Resources;
        return new AppOptions(appId: appConfigs.AppId, storageFolderName: "VpnHood", AppConfigs.IsDebugMode) {
            AppName = AppConfigs.AppName,
            // The listener a phone pairs with; without it IsRemoteAccessSupported is false.
            RemoteAccessHostProvider = () => VpnHoodAppWebServer.Instance,
            IpLocationZipData = ClientAppResources.IpLocationZipData,
            CustomData = appConfigs.CustomData,
            Resources = resources,
            PrivacyPolicyUrl = appConfigs.PrivacyPolicyUrl,
            TermsOfUseUrl = appConfigs.TermsOfUseUrl,
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
            }
        };
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