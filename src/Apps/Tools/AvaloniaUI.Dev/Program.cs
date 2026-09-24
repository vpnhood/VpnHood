using Avalonia;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Devices.Windows;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.AppUi.Common;

namespace VpnHood.App.AvaloniaUI.Dev;

// The Avalonia UI on a PC, for a developer to look at: a real VpnHoodApp on the Windows device,
// its own storage and id so it never touches the installed client, the web server up so a phone
// can pair with it exactly as with a TV, and the UI in a window that opens at its layout's size -
// a phone's, or a TV's panel with --tv. Never shipped; a head shows this UI with /avalonia-ui instead.
// "--sample-key" starts it with a server profile, which the location pages need.
// "--tv" runs it as a TV (AppFeatures.IsTv: the pairing row, the ring on arrival); without it, as
// a phone or a desktop. "--connect" runs it as the Connect product - the violet look, no keys to
// add (AppOptions.UiTheme, IsAddAccessKeySupported); without it, as the client. Connecting
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
        // The files this build's asset packages placed beside the app, read the way this
        // platform reads them: the IP-location database and the UI's store. The app extracts
        // what it must under its storage - the store once, for the in-process UI and for the
        // web host, which serves the same entries at /assets/ to a paired phone's page.
        var platformAssets = new FolderAssetProvider(AppContext.BaseDirectory);

        // the name the UI shows, as a head of that product would set it (AppFeatures.AppName is
        // this very string), so the window says which product it is running as
        var appOptions = new AppOptions(appId: "com.vpnhood.avalonia.dev", "VpnHood! Avalonia Dev", isDebugMode: true) {
            AppName = isConnect ? "VpnHood! CONNECT" : "VpnHood! CLIENT",
            CompanyName = "VpnHood",
            StorageFolderPath = storageFolderPath,
            // the documents the product links to, which every head takes from its appsettings.json:
            // without them the pages that link to them - Settings > Privacy, the paywall, the
            // drawer - have nothing to show, which is a look at a build no one ships
            PrivacyPolicyUrl = new Uri(isConnect
                ? "https://www.vpnhood.com/vpnhood-connect-privacy-policy"
                : "https://www.vpnhood.com/vpnhood-client-privacy-policy"),
            TermsOfUseUrl = new Uri(isConnect
                ? "https://www.vpnhood.com/legal/vpnhood-connect-terms-of-use"
                : "https://www.vpnhood.com/legal/vpnhood-client-terms-of-use"),
            // the product's own word in the UI - its logo, its consent summary - as a head of that
            // product names them in the store
            LogoAssetPath = isConnect ? "images/VpnHoodConnect-logo.png" : "images/VpnHoodClient-logo.png",
            PrivacyConsentAssetName = isConnect ? "privacy-consent-connect" : "privacy-consent-client",
            // left at the product default (on): the consent screen is part of what a client head
            // shows on a first run, and a run that skips it shows a build no one ships
            IsAddAccessKeySupported = !isConnect, // a connect head ships one built-in profile and takes no keys
            UiTheme = isConnect ? "violet" : "blue",
            // "--sample-key" starts with the engine's own sample token, so the run has a server
            // profile: without one the app has no location to show and no page behind the location
            // row, which is a walk through a build no one ships. It reaches no server of ours, and
            // it is the key the debug heads already embed.
            AccessKeys = args.Contains("--sample-key") ? [ClientOptions.SampleAccessKey] : [],
            IpLocationZipAsset = new Asset(platformAssets, "iplocations/IpLocations.zip"),
            UiZipAssets = [new Asset(platformAssets, "assets/ui.zip")],
            // the page a paired phone opens, and this head's own web view: the Avalonia UI's browser build
            WebRootZipAsset = new Asset(platformAssets, "assets/web-root.zip"),
            WebHostFactory = new VpnHoodAppWebHostFactory()
        };

        var device = new TvOverrideDevice(new WindowsDevice(storageFolderPath, appOptions.IsDebugMode), isTv);
        var app = VpnHoodApp.Init(device, appOptions);
        try {
            // the phone's half of the pairing: the server the pairing page hands out the address of,
            // and - a debug build - the address a browser on this PC can open to see the same UI
            // served as a page (http://<lan-ip>:9090/)
            // The UI reaches the app through its API - the same six interfaces a paired browser
            // dials over HTTP, here the app's own controllers in process - and draws from the
            // store's zip beside this executable, which the build placed there (the same files
            // the web server serves at /assets/). In process both complete at once.
            VhApp.Init(app.Api, CancellationToken.None).GetAwaiter().GetResult();
            AvaloniaUiHosting.PrepareContent<ClassicAvaloniaApp>(app.UiAssetProvider);
            VhApp.Configure(ClassicAvaloniaApp.AvailableCultures, CancellationToken.None).GetAwaiter().GetResult();
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
        return AppBuilder.Configure<ClassicAvaloniaApp>().UsePlatformDetect();
    }
}
