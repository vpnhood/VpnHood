using System.Drawing;
using System.IO;
using System.Security.Principal;
using VpnHood.Client.App.Resources;
using VpnHood.Client.App.Win.Common;
using VpnHood.Client.App.Win.Common.WpfSpa;
using VpnHood.Client.App.Win.Connect.Properties;

namespace VpnHood.Client.App.Win.Connect;

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
        // load app settings and resources
        var storageFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnHoodConnect");

        //var appSettings = AppSettings.Create();
        var resources = DefaultAppResource.Resource;
        resources.Strings.AppName = "VpnHood! CONNECT";
        resources.Colors.NavigationBarColor = Color.FromArgb(21, 14, 61);
        resources.Colors.WindowBackgroundColor = Color.FromArgb(21, 14, 61);
        resources.Colors.ProgressBarColor = Color.FromArgb(231, 180, 129);


        return new AppOptions("com.vpnhood.connect.windows") {
            UiName = "VpnHoodConnect",
            StorageFolderPath = storageFolderPath,
            DeviceId = WindowsIdentity.GetCurrent().User?.Value,
            Resource = resources,
            AccessKeys = AssemblyInfo.IsDebugMode ? [ClientOptions.SampleAccessKey] : [],
            UpdateInfoUrl = new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-win-x64.json"),
            UpdaterProvider = new WinAppUpdaterProvider(),
            IsAddAccessKeySupported = false,
            SingleLineConsoleLog = false,
            LogAnonymous = !AssemblyInfo.IsDebugMode
        };
    }
}
