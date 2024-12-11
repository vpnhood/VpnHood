using System.Drawing;
using VpnHood.Client.App.Resources;
using VpnHood.Client.App.Win.Common;
using VpnHood.Client.App.Win.Common.WpfSpa;

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
        var resources = DefaultAppResource.Resources;
        resources.Strings.AppName = IsDebugMode ? "VpnHOOD! Connect (DEBUG)" : "VpnHood! Connect";
        resources.Colors.NavigationBarColor = Color.FromArgb(21, 14, 61);
        resources.Colors.WindowBackgroundColor = Color.FromArgb(21, 14, 61);
        resources.Colors.ProgressBarColor = Color.FromArgb(231, 180, 129);

        var appConfigs = AppConfigs.Load();
        return new AppOptions("com.vpnhood.connect.windows", IsDebugMode) {
            UiName = "VpnHoodConnect",
            StorageFolderPath = AppOptions.BuildStorageFolderPath(appId: "VpnHoodConnect"),
            Resource = resources,
            AccessKeys = [appConfigs.DefaultAccessKey],
            UpdateInfoUrl = new Uri("https://github.com/vpnhood/VpnHood.Client.App.Connect/releases/latest/download/VpnHoodConnect-win-x64.json"),
            UpdaterProvider = new AdvancedInstallerUpdaterProvider(),
            IsAddAccessKeySupported = false,
            SingleLineConsoleLog = false,
            LocalSpaHostName = "my-vpnhood-connect",
            AllowEndPointTracker = true,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId
        };
    }

#if DEBUG
    public override bool IsDebugMode => true;
#else
    public override bool IsDebugMode => false;
#endif
}
