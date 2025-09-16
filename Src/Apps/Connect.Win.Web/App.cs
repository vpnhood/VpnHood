using System.Windows;
using VpnHood.App.Client;
using VpnHood.AppLib;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.AppLib.Win.Common;
using VpnHood.AppLib.Win.Common.WpfSpa;

namespace VpnHood.App.Connect.Win.Web;

public class App : Application
{
    private static AppOptions CreateAppOptions(AppConfigs appConfigs)
    {
        // load app settings and resources
        var resources = ConnectAppResources.Resources;
        resources.Strings.AppName = AppConfigs.AppName;
        return new AppOptions("com.vpnhood.connect.windows", "VpnHoodConnect", AppConfigs.IsDebugMode) {
            CustomData = appConfigs.CustomData,
            UiName = "VpnHoodConnect",
            Resources = resources,
            AccessKeys = [appConfigs.DefaultAccessKey],
            IsAddAccessKeySupported = false,
            LocalSpaHostName = "my-vpnhood-connect",
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            PremiumFeatures = ConnectAppResources.PremiumFeatures,
            AllowRecommendUserReviewByServer = true,
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
        var appConfigs = AppConfigs.Load();
        VpnHoodAppWpfSpa.Init(() => CreateAppOptions(appConfigs),
            spaListenToAllIps: appConfigs.SpaListenToAllIps,
            spaDefaultPort: appConfigs.SpaDefaultPort,
            args: Environment.GetCommandLineArgs());
    }

    [STAThread]
    public static void Main(string[] args)
    {
        var app = new App();
        app.Run();
    }
}