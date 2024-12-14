using System.Security.Principal;
using VpnHood.AppFramework;
using VpnHood.AppFramework.Resources;
using VpnHood.AppFramework.Win.Common;
using VpnHood.AppFramework.Win.Common.WpfSpa;

namespace VpnHood.Apps.Client.Win.Web;

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

        return new AppOptions("com.vpnhood.client.windows", AppConfigs.IsDebugMode) {
            DeviceId = WindowsIdentity.GetCurrent().User?.Value,
            StorageFolderPath = AppOptions.BuildStorageFolderPath(appId: "VpnHood"),
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
