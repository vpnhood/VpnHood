using System.Security.Principal;
using System.Windows;
using VpnHood.AppLib;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.AppLib.Win.Common;
using VpnHood.AppLib.Win.Common.WpfSpa;

namespace VpnHood.App.Client.Win.Web;

public class App : Application
{
    private static AppOptions CreateAppOptions()
    {
        var appConfigs = AppConfigs.Load();
        var resources = ClientAppResources.Resources;
        resources.Strings.AppName = AppConfigs.AppName;

        return new AppOptions(appConfigs.AppId, appConfigs.StorageFolderName, AppConfigs.IsDebugMode) {
            DeviceId = WindowsIdentity.GetCurrent().User?.Value,
            Resources = resources,
            CustomData = appConfigs.CustomData,
            WebUiPort = appConfigs.WebUiPort,
            AccessKeys = appConfigs.DefaultAccessKey != null ? [appConfigs.DefaultAccessKey] : [],
            IsAddAccessKeySupported = true,
            IsLocalNetworkSupported = true,
            RemoteSettingsUrl = appConfigs.RemoteSettingsUrl,
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            AllowRecommendUserReviewByServer = false,
            LogServiceOptions = {
                SingleLineConsole = false
            },
            UpdaterOptions = new AppUpdaterOptions {
                UpdateInfoUrl = appConfigs.UpdateInfoUrl,
                UpdaterProvider = new AdvancedInstallerUpdaterProvider(),
                PromptDelay = TimeSpan.FromDays(1)
            }
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // call base first to init app resources
        base.OnStartup(e);

        // load app configs
        VpnHoodAppWpfSpa.Init(CreateAppOptions, args: Environment.GetCommandLineArgs());
    }

    [STAThread]
    public static void Main(string[] args)
    {
        var app = new App();
        app.Run();
    }


}