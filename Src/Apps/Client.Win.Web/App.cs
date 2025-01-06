using System.Security.Principal;
using VpnHood.AppLib;
using VpnHood.AppLib.Resources;
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

        var resources = DefaultAppResource.Resources;
        resources.Strings.AppName = appConfigs.AppName;

        return new AppOptions(appConfigs.AppId, appConfigs.StorageFolderName, AppConfigs.IsDebugMode) {
            DeviceId = WindowsIdentity.GetCurrent().User?.Value,
            Resource = resources,
            AccessKeys = AppConfigs.IsDebugMode ? [appConfigs.DefaultAccessKey] : [],
            UpdateInfoUrl = appConfigs.UpdateInfoUrl,
            UpdaterProvider = new AdvancedInstallerUpdaterProvider(),
            IsAddAccessKeySupported = true,
            SingleLineConsoleLog = false,
            LocalSpaHostName = "my-vpnhood"
        };
    }
}
