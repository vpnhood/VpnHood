using System.Drawing;
using System.Security.Principal;
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

    protected override AppOptionsEx CreateAppOptions()
    {
        // load app settings and resources
        var resources = DefaultAppResource.Resources;
        resources.Strings.AppName = "VpnHood! CONNECT";
        resources.Colors.NavigationBarColor = Color.FromArgb(21, 14, 61);
        resources.Colors.WindowBackgroundColor = Color.FromArgb(21, 14, 61);
        resources.Colors.ProgressBarColor = Color.FromArgb(231, 180, 129);

        var appConfigs = AppConfigs.Load();
        return new AppOptionsEx("com.vpnhood.connect.windows") {
            UiName = "VpnHoodConnect",
            StorageFolderPath = AppOptions.BuildStorageFolderPath(appId: "VpnHoodConnect"),
            DeviceId = WindowsIdentity.GetCurrent().User?.Value,
            Resource = resources,
            AccessKeys = [appConfigs.DefaultAccessKey],
            UpdateInfoUrl = appConfigs.UpdateInfoUrl != null ? new Uri(appConfigs.UpdateInfoUrl) : null,
            UpdaterProvider = new WinAppUpdaterProvider(),
            IsAddAccessKeySupported = false,
            SingleLineConsoleLog = false,
            LogAnonymous = !AppConfigs.IsDebugMode,
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            LocalSpaHostName = "my-vpnhood-connect",
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            ListenToAllIps = appConfigs.ListenToAllIps,
            DefaultSpaPort = appConfigs.DefaultSpaPort,
        };
    }
}
