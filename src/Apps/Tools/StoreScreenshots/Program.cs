using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using VpnHood.AppLib.Api;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Views;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.App.StoreScreenshots;

// The Avalonia UI drawn into a PNG, for the store listings: the UI over a fixture of the app's API
// - the same fixture.json the web UI's screenshot engine answers /api/** from - in a headless top
// level sized as the device, at the device's pixel ratio. No app runs; the pages read VhApp, and
// VhApp is given the fixture the way a head gives it the app (AvaloniaDesktopHost.Run's second
// overload, whose API came over HTTP from another process).
//
//   VpnHoodStoreScreenshots --fixture <fixture.json> --assets <store folder or ui.zip> --route /protocols
//       --width 360 --height 692 --scale 8 --out raw/android-phone/4_en-US.png [--culture fa] [--hide EnabledItem/Chip]
//
// One process per shot, deliberately: VhApp and the UI's stores are static, so a page that changed
// a setting cannot leak into the next shot, and the engine runs shots in parallel as processes the
// way it runs browser contexts. The exit code says whether the PNG may be used.
//
// The output is lines of `tag       text` (the tag padded to nine columns) that the engine reads:
// captured, opened, hidden, filled, warning and unmocked on stdout, FAILED on stderr.
internal static class Program
{
    public static int Main(string[] args)
    {
        // `generate` makes a whole set (GenerateCommand) and runs the other two itself: `shot`
        // draws one screen with the UI, `frame` dresses one capture in its device. A command line
        // that names no command takes one shot, which is what the web UI's engine spawns while it
        // is being retired.
        switch (args.FirstOrDefault()) {
            case "generate":
                return GenerateCommand.Run(args.Skip(1).ToArray());
            case "frame":
                return FrameCommand.Run(args.Skip(1).ToArray());
            case "shot":
                args = args.Skip(1).ToArray();
                break;
        }

        ShotOptions options;
        try {
            options = ShotOptions.Parse(args);
        }
        catch (ArgumentException ex) {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        // what goes wrong before the UI is up is reported the way the shot itself would be: one
        // line on stderr, exit 1 - the engine relays it under the shot's name
        try {
            return Run(args, options);
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"FAILED    {ex.Message}");
            return 1;
        }
    }

    private static int Run(string[] args, ShotOptions options)
    {
        var log = new CapturingLogger();
        VhLogger.Instance = log;

        var fixture = StoreFixture.Load(options.FixturePath, options.Culture);
        foreach (var filled in fixture.Filled)
            Console.WriteLine($"filled    {filled} (the fixture predates this member of the contract - re-record it)");
        if (fixture.ContractDrift != null)
            Console.WriteLine($"warning   the fixture is behind the API contract: {fixture.ContractDrift}");

        var app = new FakeAppApi(fixture.Info, fixture.InstalledApps);
        var api = new VpnHoodApi(app, new FakeClientProfilesApi(app), new FakeAccountApi(), new FakeBillingApi(), new FakeIntentsApi(), new FakeProxyEndPointsApi());

        // The head's three steps before Avalonia starts (AvaloniaDesktopHost.Run): the API, the
        // content out of the store, the languages the UI has words for.
        VhApp.Init(api, CancellationToken.None).GetAwaiter().GetResult();
        AvaloniaUiHosting.PrepareContent<ClassicAvaloniaApp>(Store.Open(options.AssetsPath));
        VhApp.Configure(ClassicAvaloniaApp.AvailableCultures, CancellationToken.None).GetAwaiter().GetResult();

        // Strings falls back to the language alone and then to English for a culture the store has
        // no words for, which on a screen is a listing in the wrong language: refused by name here.
        if (options.Culture != null && !ClassicAvaloniaApp.AvailableCultures.Contains(options.Culture, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"The store has no words for '{options.Culture}'. It has: {string.Join(", ", ClassicAvaloniaApp.AvailableCultures)}.");

        var lifetime = new ClassicDesktopStyleApplicationLifetime { Args = args, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AppBuilder.Configure<ClassicAvaloniaApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .SetupWithLifetime(lifetime);

        // the UI made its window at a television's size (VpnHoodAvaloniaAppBase); the device's is asked for
        var window = lifetime.MainWindow ?? throw new InvalidOperationException("The UI has made no main window.");
        window.MinWidth = 0;
        window.MinHeight = 0;
        window.Width = options.Width;
        window.Height = options.Height;
        var view = window.Content as MainView ?? throw new InvalidOperationException("The window does not hold the UI's main view.");

        var runner = new ShotRunner(options, window, view, log);
        Dispatcher.UIThread.Post(async () => lifetime.Shutdown(await runner.RunAsync()), DispatcherPriority.Background);
        return lifetime.Start(args);
    }
}
