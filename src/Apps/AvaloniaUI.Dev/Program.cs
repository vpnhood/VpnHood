using Avalonia;
using VpnHood.App.Client;
using VpnHood.AppLib;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.WebServer;
using VpnHood.Core.Client.Devices.Win;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.App.AvaloniaUI.Dev;

// The Avalonia UI on a PC, for a developer to look at: a real VpnHoodApp on the Windows device,
// its own storage and id so it never touches the installed client, the web server up so a phone
// can pair with it exactly as with a TV, and the UI in a window that opens at a TV's size and
// resizes down to a phone's. Never shipped; a head shows this UI with /avalonia-ui instead.
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

        // "--storage <name>" gives the run its own settings, log and profiles, so a second window
        // can be driven while someone is using one - and a name never used before is a first run,
        // which is the only way to see what a new install sees.
        var storageFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ValueOf(args, "--storage") ?? "VpnHood.AvaloniaDev");
        var resources = ClientAppResources.Resources;
        // the name the UI shows, as a head of that product would set it (AppFeatures.AppName is
        // this very string), so the window says which product it is running as
        resources.Strings.AppName = isConnect ? "VpnHood! CONNECT" : "VpnHood! CLIENT";

        var appOptions = new AppOptions(appId: "com.vpnhood.avalonia.dev", "VpnHood! Avalonia Dev", isDebugMode: true) {
            StorageFolderPath = storageFolderPath,
            Resources = resources,
            // the documents the product links to, which every head takes from its appsettings.json:
            // without them the pages that link to them - Settings > Privacy, the paywall, the
            // drawer - have nothing to show, which is a look at a build no one ships
            PrivacyPolicyUrl = new Uri(isConnect
                ? "https://www.vpnhood.com/vpnhood-connect-privacy-policy"
                : "https://www.vpnhood.com/vpnhood-client-privacy-policy"),
            TermsOfUseUrl = new Uri(isConnect
                ? "https://www.vpnhood.com/legal/vpnhood-connect-terms-of-use"
                : "https://www.vpnhood.com/legal/vpnhood-client-terms-of-use"),
            // left at the product default (on): the consent screen is part of what a client head
            // shows on a first run, and a run that skips it shows a build no one ships
            IsAddAccessKeySupported = !isConnect, // a connect head ships one built-in profile and takes no keys
            UiName = isConnect ? AppProduct.ConnectUiName : null
        };

        var device = new TvOverrideDevice(new WinDevice(storageFolderPath, appOptions.IsDebugMode), isTv);
        var app = VpnHoodApp.Init(device, appOptions);
        try {
            // the phone's half of the pairing: the server the pairing page hands out the address of,
            // and - a debug build - the address a browser on this PC can open to see the same UI
            // served as a page (http://<lan-ip>:9090/)
            using var webServer = VpnHoodAppWebServer.Init(app);

            // The UI reaches the app through its API - the same six interfaces a paired browser
            // dials over HTTP, here the app's own controllers in process - and draws from the
            // assets folder beside this executable, which the build placed there (the same files
            // the web server serves at /assets/). In process both complete at once.
            AppData.Init(InProcessAppApi.Create(app), CancellationToken.None).GetAwaiter().GetResult();
            AppData.Configure(CancellationToken.None).GetAwaiter().GetResult();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally {
            app.Dispose();
        }
    }

    // the word after a flag, "--storage other"; null when the flag is absent or ends the line
    private static string? ValueOf(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    // Avalonia's designer and previewer look for this by name.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<VpnHoodAvaloniaApp>().UsePlatformDetect();
    }
}
