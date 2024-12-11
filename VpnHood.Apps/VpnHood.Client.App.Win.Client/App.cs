using System.Security.Principal;
using VpnHood.Client.App.Resources;
using VpnHood.Client.App.Win.Common;
using VpnHood.Client.App.Win.Common.WpfSpa;

namespace VpnHood.Client.App.Win.Client;

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
        var resources = DefaultAppResource.Resources;
        resources.Strings.AppName = IsDebugMode ? "VpnHOOD! Client (DEBUG)" : "VpnHood! Client";

        return new AppOptions("com.vpnhood.client.windows", IsDebugMode) {
            DeviceId = WindowsIdentity.GetCurrent().User?.Value,
            StorageFolderPath = AppOptions.BuildStorageFolderPath(appId: "VpnHood"),
            Resource = resources,
            AccessKeys = IsDebugMode ? [ClientOptions.SampleAccessKey] : [],
            UpdateInfoUrl = new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-win-x64.json"),
            UpdaterProvider = new AdvancedInstallerUpdaterProvider(),
            IsAddAccessKeySupported = true,
            SingleLineConsoleLog = false,
            LocalSpaHostName = "my-vpnhood"
        };
    }

#if DEBUG
    public override bool IsDebugMode => true;
#else
    public override bool IsDebugMode => false;
#endif
}
