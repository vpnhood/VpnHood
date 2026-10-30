using VpnHood.AppUi.Hosting.Avalonia.Ios;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using Foundation;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Ios;
using VpnHood.AppLib.Stores.AppStore;
using VpnHood.AppLib.App.Services.Ads;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.App.Client.Tvos.Apple;

// The Avalonia UI's application delegate on an Apple TV, as the iOS head's: it starts the app from the
// params below as launching finishes, then the UI, which takes its TV layout here (IosDevice.IsTv).
[Register("AppDelegate")]
public class AppDelegate : IosAvaloniaAppDelegate<ClassicAvaloniaApp>
{
    // The iOS app's folder name (Client.Ios.Apple), frozen with it: the same app, in a sandbox of its own.
    // ReSharper disable once HeuristicUnreachableCode
    private const string StorageFolderName = AppConstants.IsDebugMode ? "VpnHood! Client (DEBUG)" : "VpnHood! Client";

    // The platform builds the device from the App Group and the extension's bundle id (VpnHoodIosApp).
    protected override IosInitParams CreateInitParams()
    {
        return new IosInitParams {
            // the bundle's own id, which the build took from the app's identity
            AppId = NSBundle.MainBundle.BundleIdentifier ??
                    throw new InvalidOperationException("The app's bundle has no identifier."),
            AppName = AppConstants.AppName,
            AppVersion = Version.Parse(AppConstants.AppVersion),
            StorageFolderName = StorageFolderName,
            AppGroupId = AppConstants.AppGroupId,
            ProviderBundleId = AppConstants.ProviderBundleId,
            AppOptionsFactory = BuildAppOptions
        };
    }

    // The product's options and the App Store's lines on top, as the iOS head's (whose comments say why),
    // but for the in-app rating prompt, which StoreKit has none of for tvOS.
    private static AppOptions BuildAppOptions(AppOptionsContext context)
    {
        var appConfigs = ClientAppConfigs.Load();
        var options = ClientAppOptions.Create(context, appConfigs);

        // App Review 5.4: no data to a third party
        options.TrackerFactories = [];
        options.AllowEndPointTracker = false;
        options.Links = options.Links with {
            TermsOfUse = options.Links.TermsOfUse ??
                         new Uri("https://www.apple.com/legal/internet-services/itunes/dev/stdeula/")
        };
        options.Transport = ClientTransportOptions.ForCurrentPlatform();
        options.LogServiceOptions = new LogServiceOptions {
            MinLogLevel = LogLevel.Information
        };
        options.AdOptions = new AppAdOptions {
            PreloadAd = false
        };
        // the store is the only install channel
        options.UpdaterOptions = new AppUpdaterOptions {
            UpdaterProvider = new AppStoreAppUpdaterProvider(options.AppVersion)
        };
        return options;
    }
}
