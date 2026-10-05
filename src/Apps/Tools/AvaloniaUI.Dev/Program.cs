using Avalonia;
using VpnHood.App.Connect;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Avalonia.Desktop;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Devices.Windows;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.App.AvaloniaUI.Dev;

// The Avalonia UI on a PC, for a developer to look at: a real VpnHoodApp on the Windows device,
// its own storage and id so it never touches the installed client, the web server up so a phone
// can pair with it exactly as with a TV, and the UI in a window that opens at its layout's size -
// a phone's, or a TV's panel with --tv. Never shipped.
// "--sample-key" starts it with a server profile, which the location pages need.
// "--tv" runs it as a TV (AppFeatures.IsTv: the pairing row, the ring on arrival); without it, as
// a phone or a desktop. "--connect" runs it as the Connect product - the violet look, no keys to
// add (AppOptions.UiTheme, IsAddAccessKeySupported); without it, as the client. "--store google"
// or "--store apple" runs it as Connect's build for that store - its premium tier, its sign-in and
// its paywall - against a stand-in account (DevAccountProvider) that sells sample plans and charges
// no one; with --tv, as Google TV or Apple TV shows them. Connecting needs the WinDivert driver, so a
// plain run shows the walk and the pairing; run elevated to connect as well.
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VhLogger.AddProvider(new ConsoleLoggerProvider());
        var isTv = args.Contains("--tv");
        var store = ValueOf(args, "--store");
        var isConnect = args.Contains("--connect") || store != null; // only Connect sells

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
        var appOptionsContext = new AppOptionsContext {
            AppId = "com.vpnhood.avalonia.dev",
            AppName = isConnect ? "VpnHood! CONNECT" : "VpnHood! CLIENT",
            StoragePath = storageFolderPath,
            PackagedAssetProvider = platformAssets
        };

        var appOptions = new AppOptions(appOptionsContext, isDebugMode: true) {
            PackageTitle = isConnect ? "VpnHoodConnect" : "VpnHoodClient",
            CompanyName = "VpnHood",
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
            LogoAssetPath = isConnect ? "images/logo-connect.png" : "images/logo-client.png",
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
            // a store build: the store's account and tier as that head sets them - Play lets a buyer type
            // a code in, the App Store does not - and no terms screen, which a store's install replaces;
            // Google Play has no subscriptions screen on a TV
            AccountProvider = store switch {
                null => null,
                "google" => new DevAccountProvider(StoreIds.GooglePlay, AuthProviders.Google, isSubscriptionManagementSupported: !isTv),
                "apple" => new DevAccountProvider(StoreIds.AppStore, AuthProviders.Apple, isSubscriptionManagementSupported: true),
                _ => throw new ArgumentException($"--store {store}: google or apple.")
            },
            Premium = store == null ? null : ConnectAppOptions.CreatePremium(allowImportAccessCode: store == "google", isPurchaseUrlSupported: false),
            IsLicenseAgreementRequired = store == null,
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
            // the web server serves at /assets/). It runs in the window a desktop head shows it
            // in, which is also the app's UI context: a store's sign-in and purchase ask for one.
            AvaloniaDesktopHost.Run<ClassicAvaloniaApp>(args, showWindow: true, app.Api, app.UiAssetProvider,
                exitOnClose: true);
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
