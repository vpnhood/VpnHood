using System.Security.Principal;
using VpnHood.AppLib;
using VpnHood.AppLib.Win.Common;
using VpnHood.AppLib.Win.Common.WpfSpa;

namespace VpnHood.App.Client.Win.Web;

public class App : VpnHoodWpfSpaApp
{
    [STAThread]
    public static void Main()
    {
        var app = new App();
        app.Run();
    }

    public override bool SpaListenToAllIps => AppConfigs.Instance.SpaListenToAllIps;
    public override int? SpaDefaultPort => AppConfigs.Instance.SpaDefaultPort;

    protected override AppOptions CreateAppOptions()
    {
        var appConfigs = AppConfigs.Load();

        var resources = ClientAppResources.Resources;
        resources.Strings.AppName = AppConfigs.AppName;

        return new AppOptions(appConfigs.AppId, appConfigs.StorageFolderName, AppConfigs.IsDebugMode) {
            CustomData = appConfigs.CustomData,
            DeviceId = WindowsIdentity.GetCurrent().User?.Value,
            Resources = resources,
            AccessKeys = AppConfigs.IsDebug ? [appConfigs.DefaultAccessKey] : [],
            UpdateInfoUrl = appConfigs.UpdateInfoUrl,
            UpdaterProvider = new AdvancedInstallerUpdaterProvider(),
            IsAddAccessKeySupported = true,
            IsLocalNetworkSupported = true,
            LocalSpaHostName = "my-vpnhood",
            AllowRecommendUserReviewByServer = false,
            LogServiceOptions = {
                SingleLineConsole = false
            }
        };
    }
}