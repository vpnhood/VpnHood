using System.Security.Principal;
using VpnHood.Client.App.Resources;
using VpnHood.Client.App.Win.Common;
using VpnHood.Client.App.Win.Common.WpfSpa;
using VpnHood.Client.App.Win.Properties;

namespace VpnHood.Client.App.Win;

public class App : VpnHoodWpfSpaApp
{
    [STAThread]
    public static void Main()
    {
        var app = new App();
        app.Run();
    }

    protected override AppOptions CreateAppOptions()
    {
        return new AppOptions("com.vpnhood.client.windows") {
            DeviceId = WindowsIdentity.GetCurrent().User?.Value,
            StorageFolderPath = AppOptions.BuildStorageFolderPath(appId: "VpnHood"),
            Resource = DefaultAppResource.Resource,
            AccessKeys = AssemblyInfo.IsDebugMode ? [ClientOptions.SampleAccessKey] : [],
            UpdateInfoUrl =
                new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-win-x64.json"),
            UpdaterProvider = new WinAppUpdaterProvider(),
            IsAddAccessKeySupported = true,
            SingleLineConsoleLog = false,
            LogAnonymous = !AssemblyInfo.IsDebugMode,
            LocalSpaHostName = "my-vpnhood"
        };
    }
}
