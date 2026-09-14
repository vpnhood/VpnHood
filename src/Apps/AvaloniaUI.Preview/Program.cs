using Avalonia;
using VpnHood.App.Client;
using VpnHood.AppLib;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.WebServer;
using VpnHood.Core.Client.Devices.Win;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.App.AvaloniaUI.Preview;

// The Avalonia UI on a PC: a real VpnHoodApp on the Windows device, its own storage and id so it
// never touches the installed client, the web server up so a phone can pair with it exactly as
// with a TV, and the UI in a window that opens at a TV's size and resizes down to a phone's.
// "--tv" runs it as a TV (AppFeatures.IsTv: the pairing row, the ring on arrival); without it, as
// a phone or a desktop. "--connect" runs it as the Connect product, which is what picks the theme
// (AppOptions.UiName); without it, as the client, as any head that names no product. Connecting
// needs the WinDivert driver, so a plain run shows the walk and the pairing; run elevated to
// connect as well.
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VhLogger.Instance = VhLogger.CreateConsoleLogger();
        var isTv = args.Contains("--tv");
        var isConnect = args.Contains("--connect");

        var storageFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnHood.AvaloniaPreview");
        var resources = ClientAppResources.Resources;
        // the name the UI shows, as a head of that product would set it (AppFeatures.AppName is
        // this very string), so the preview says which product it is running as
        resources.Strings.AppName = isConnect ? "VpnHood! CONNECT" : "VpnHood! CLIENT";

        var appOptions = new AppOptions(appId: "com.vpnhood.avalonia.preview", "VpnHood! Avalonia Preview", isDebugMode: true) {
            StorageFolderPath = storageFolderPath,
            Resources = resources,
            IsLicenseAgreementRequired = false,
            IsAddAccessKeySupported = !isConnect, // a connect head ships one built-in profile and takes no keys
            UiName = isConnect ? AppProduct.ConnectUiName : null
        };

        var device = new PreviewDevice(new WinDevice(storageFolderPath, appOptions.IsDebugMode), isTv);
        var app = VpnHoodApp.Init(device, appOptions);
        try {
            // the phone's half of the pairing: the server the pairing page hands out the address of
            using var webServer = VpnHoodAppWebServer.Init(app);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally {
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    // Avalonia's designer and previewer look for this by name.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<VpnHoodAvaloniaApp>().UsePlatformDetect();
    }
}
