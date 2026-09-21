using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Toolkit.Assets;
using VpnHood.AppLib.Api.Premium;
using VpnHood.AppLib.Services.Ads;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Core.Client.Devices;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Test;
using VpnHood.Test.Device;
using VpnHood.Test.Providers;

using VpnHood.Core.Client.Abstractions;

namespace VpnHood.AppLib.Test;

public class TestAppHelper : TestHelper
{
    // The asset folder the VpnHood.Core.IpLocations.Assets.Ip2LocationLite package's build places
    // assembly. The package ships no code, so the name is the contract - named here for the tests
    // the way each head names it for itself.
    public const string IpLocationAssetPath = "iplocations/IpLocations.zip";

    // the tests run beside their own files, so the plain provider is the right one
    public static readonly IAssetProvider AssetProvider = new FolderAssetProvider(AppContext.BaseDirectory);

    // isDebugMode: false stands for a release build where the test needs the difference, e.g. the web
    // server's remote access. The tracker and log options below are explicit, so the flag changes nothing else.
    public AppOptions CreateAppOptions(bool isDebugMode = true)
    {
        var appOptions = new AppOptions("com.vpnhood.client.test", "VpnHoodClient.Test", isDebugMode) {
            AppName = "VpnHood! Test",
            CompanyName = "VpnHood",
            LogoAssetPath = "images/VpnHoodClient-logo.png",
            PrivacyConsentAssetName = "privacy-consent-client",
            IpLocationZipAsset = new Asset(AssetProvider, IpLocationAssetPath),
            IsSingleton = false, // tests run many concurrent apps in one process
            // the test app stands for a CONNECT-like head no store forbids anything to; store-build
            // restrictions and the premium-less CLIENT shape are exercised by the tests that
            // reassign this block (or null it) explicitly
            Premium = new AppPremiumOptions {
                AllowImportAccessCode = true,
                IsPurchaseUrlSupported = true
            },
            StorageFolderPath = Path.Combine(WorkingPath, "AppData_" + Guid.CreateVersion7()),
            DeviceUiProvider = new TestDeviceUiProvider(),
            EventWatcherInterval = TimeSpan.FromMilliseconds(200), // no SPA in test, so we need to use event watcher
            Ga4MeasurementId = null,
            TrackerFactory = new TestTrackerFactory(),
            AllowEndPointTracker = true,
            AutoDiagnose = false,
            DisconnectOnDispose = true,
            ConnectTimeout = TimeSpan.FromSeconds(5).WhenNoDebugger(),
            Transport = new ClientTransportOptions {
                SessionTimeout = TimeSpan.FromSeconds(2),
                ServerQueryTimeout = TimeSpan.FromSeconds(2),
                TcpConnectTimeout = TimeSpan.FromSeconds(2).WhenNoDebugger()
            },
            AdOptions = new AppAdOptions {
                ShowAdPostDelay = TimeSpan.Zero,
                LoadAdPostDelay = TimeSpan.Zero,
                ExtendByRewardedAdThreshold = TimeSpan.Zero,
                RejectAdBlocker = true,
                AllowedPrivateDnsProviders = ["dns.google", "dns.test"]
            },
            LogServiceOptions = {
                // apps would fight over the process-wide VhLogger; tests asserting State.LogExists opt back in
                Enabled = false,
            }
        };

        return appOptions;
    }

    // The smallest page a web host will serve - an index.html - as the zip a head ships, so a test
    // can tell the page from the API's replies by its title.
    public IAsset CreateWebRootZip(string title)
    {
        Directory.CreateDirectory(WorkingPath);
        var path = Path.Combine(WorkingPath, $"web-root_{Guid.CreateVersion7()}.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create)) {
            using var writer = new StreamWriter(archive.CreateEntry("index.html").Open());
            writer.Write($"<html><title>{title}</title></html>");
        }

        return new FileAsset(path);
    }

    public VpnHoodApp CreateClientApp(AppOptions? appOptions = null, IDevice? device = null)
    {
        appOptions ??= CreateAppOptions();
        device ??= new TestDevice(this, _ => new TestNullVpnAdapter());

        //create app; not registered as the singleton, so keep the returned instance
        var clientApp = VpnHoodApp.Init(device, appOptions);
        clientApp.Diagnoser.HttpTimeout = TimeSpan.FromSeconds(2);
        clientApp.Diagnoser.NsTimeout = TimeSpan.FromSeconds(2);
        clientApp.UserSettings.SplitTunneling.UseIpViaDevice = true;
        clientApp.UserSettings.SplitTunneling.UseIpViaApp = true;
        clientApp.UserSettings.UseTcpProxy = true;
        clientApp.SettingsService.SplitIpViaDeviceSettings.Includes = TestIps.AllRemoteTestIps.ToOrderedIpRanges().ToText();
        clientApp.UserSettings.LogAnonymous = false;

        AppUiContext.Context = new TestAppUiContext();
        return clientApp;
    }

    private static string GenerateSecureRandomDigits(int length)
    {
        var result = new StringBuilder(length);
        using var rng = RandomNumberGenerator.Create();
        var buffer = new byte[1];
        while (result.Length < length) {
            rng.GetBytes(buffer);
            var digit = buffer[0] % 10;
            result.Append(digit);
        }

        return result.ToString();
    }

    public string BuildAccessCode()
    {
        return AccessCodeUtils.Build(GenerateSecureRandomDigits(18));
    }

    public override void Dispose()
    {
        // AppRegionInfo is process-global; don't let a test's country override leak into the next test
        AppRegionInfo.Reset();
        base.Dispose();
    }
}