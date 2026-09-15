using Android.Runtime;
using Avalonia.Android;
using VpnHood.AppLib;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.Droid.Common;
using VpnHood.AppLib.Droid.Common.Constants;
using VpnHood.AppLib.Droid.GooglePlay;
using VpnHood.AppLib.Services.Updaters;

namespace VpnHood.App.Client.Droid.Google;

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
    : AvaloniaAndroidApplication<VpnHoodAvaloniaApp>(javaReference, transfer)
{
    private static AppOptions CreateAppOptions()
    {
        var appConfigs = AppConfigs.Load();
        var resources = ClientAppResources.Resources;
        resources.Strings.AppName = AppConfigs.AppName;

        return new AppOptions(appId: appConfigs.AppId, "VpnHood", AppConfigs.IsDebugMode) {
            CustomData = appConfigs.CustomData,
            Resources = resources,
            PrivacyPolicyUrl = appConfigs.PrivacyPolicyUrl,
            TermsOfUseUrl = appConfigs.TermsOfUseUrl,
            // The store already took this acceptance at install - see AppOptions.
            IsLicenseAgreementRequired = false,
            AccessKeys = appConfigs.DefaultAccessKey != null ? [appConfigs.DefaultAccessKey] : [],
            IsAddAccessKeySupported = true,
            AdjustForSystemBars = false,
            UserReviewProvider = new GooglePlayInAppUserReviewProvider(),
            AllowEndPointStrategy = true,
            WebUiPort = appConfigs.WebUiPort,
            AllowRecommendUserReviewByServer = false,
            UpdaterOptions = new AppUpdaterOptions {
                UpdateInfoUrl = appConfigs.UpdateInfoUrl,
                UpdaterProvider = new GooglePlayAppUpdaterProvider(),
                PromptDelay = TimeSpan.FromDays(3)
            }
        };
    }

    public override void OnCreate()
    {
        VpnHoodAndroidApp.Init(CreateAppOptions);
        base.OnCreate();
    }

    public override void OnTerminate()
    {
        if (VpnHoodAndroidApp.IsInit)
            VpnHoodAndroidApp.Instance.Dispose();
    }
}