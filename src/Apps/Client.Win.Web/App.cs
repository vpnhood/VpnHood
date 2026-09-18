using System.Security.Principal;
using System.Windows;
using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppLib;
using VpnHood.AppUi.Hosting.Avalonia.Desktop;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.AppLib.Utils;
using VpnHood.AppLib.Win.Common;
using VpnHood.AppUi.Hosting.WebView.Windows;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.AppLib.Api.WebHost;

namespace VpnHood.App.Client.Win.Web;

public class App : Application
{
    private static AppOptions CreateAppOptions()
    {
        var appConfigs = AppConfigs.Load();
        var resources = ClientAppResources.Resources;

        return new AppOptions(appConfigs.AppId, appConfigs.StorageFolderName, AppConfigs.IsDebugMode) {
            AppName = AppConfigs.AppName,
            // The listener a phone pairs with; without it IsRemoteAccessSupported is false.
            RemoteAccessHostProvider = () => VpnHoodAppWebHost.Instance,
            IpLocationZipData = ClientAppResources.IpLocationZipData,
            DeviceId = WindowsIdentity.GetCurrent().User?.Value,
            Resources = resources,
            PrivacyPolicyUrl = appConfigs.PrivacyPolicyUrl,
            TermsOfUseUrl = appConfigs.TermsOfUseUrl,
            CustomData = appConfigs.CustomData,
            AccessKeys = appConfigs.DefaultAccessKey != null ? [appConfigs.DefaultAccessKey] : [],
            IsAddAccessKeySupported = true,
            RemoteSettingsUrl = appConfigs.RemoteSettingsUrl,
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            WebUiPort = appConfigs.WebUiPort,
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

        // the web UI, in this application's window
        VpnHoodAppWpf.Init();
    }

    [STAThread]
    public static void Main(string[] args)
    {
        // The app first, on its own; then the UI framework by the app's own setting: WPF hosts the
        // web UI, Avalonia the native one when the debug command forces it.
        try {
            VpnHoodAppWin.Init(CreateAppOptions, args, () => ClientAppResources.GetWebRootZip(VpnHoodApp.Instance.HasDebugCommand(DebugCommands.AvaloniaUi)));
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not run the app.");
            return;
        }

        if (VpnHoodApp.Instance.HasDebugCommand(DebugCommands.AvaloniaUi))
            RunAvaloniaUi(args);
        else
            new App().Run();
    }

    // The Avalonia UI in its own window, opened from the tray as the web UI's is; Exit there ends
    // it, with the app.
    private static void RunAvaloniaUi(string[] args)
    {
        var appWin = VpnHoodAppWin.Instance;
        appWin.OpenMainWindowRequested += (_, _) => AvaloniaDesktopHost.ShowMainWindow();
        appWin.ExitRequested += (_, _) => AvaloniaDesktopHost.Shutdown();
        try {
            AvaloniaDesktopHost.Run<ClassicAvaloniaApp>(args, appWin.ShowWindowAfterStart);
        }
        finally {
            appWin.Dispose();
        }
    }
}