using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.Device;
using VpnHood.AppLib.Services.Ads;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib;

public class AppOptions(string appId, string storageFolderName, bool isDebugMode)
{
    public static string BuildStorageFolderPath(string subFolder)
    {
        // default
        var baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (OperatingSystem.IsLinux()) {
            // get current executable folder
            baseFolder = Path.GetDirectoryName(Environment.ProcessPath!)!;
        }

        return Path.Combine(baseFolder, subFolder);
    }

    public string AppId => appId;
    public bool IsDebugMode => isDebugMode;

    // Tests run many concurrent apps in one process, so they opt out of the singleton
    // registration; production keeps the single-instance guarantee and VpnHoodApp.Instance.
    internal bool IsSingleton { get; set; } = true;
    public string StorageFolderPath { get; set; } = BuildStorageFolderPath(storageFolderName);

    // Transport tuning handed to the client untouched. Its timeouts default themselves; the buffer
    // and count knobs stay null so each core component applies its own default at first use.
    // Starts at the preset for this platform, so a memory-capped head is safe without opting in.
    public ClientTransportOptions Transport { get; set; } = ClientTransportOptions.ForCurrentPlatform();
    public AppUpdaterOptions? UpdaterOptions { get; set; }
    public AppResources Resources { get; set; } = new();

    // ReSharper disable once StringLiteralTypo
    public string? Ga4MeasurementId { get; set; } = "G-4LE99XKZYE";
    public string? UiName { get; set; }
    public bool IsAddAccessKeySupported { get; set; } = true;

    // This build's premium tier, or null when the product has none (the CLIENT apps): the app then
    // runs as the FULL app — every feature on, nothing sold, no promotion — however the server's
    // client policies tempt it. See AppPremiumOptions for the per-member rules.
    public AppPremiumOptions? Premium { get; set; }
    public string[] AccessKeys { get; set; } = [];
    public IDeviceUiProvider? DeviceUiProvider { get; set; }
    public IAppCultureProvider? CultureProvider { get; set; }
    public IAccountProvider? AccountProvider { get; set; }
    public IAppUserReviewProvider? UserReviewProvider { get; set; }
    public IReadOnlyList<AppAdProviderItem> AdProviderItems { get; set; } = [];
    public ITrackerFactory? TrackerFactory { get; set; }

    public bool? LogAnonymous { get; set; } =
        isDebugMode ? false : null; // it follows user's settings if it set to null

    // The whole-connect deadline for the app (tripled when diagnosing) - not the per-TCP connect
    // timeout, which is Transport.TcpConnectTimeout.
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromMinutes(4).WhenNoDebugger();
    public bool AutoDiagnose { get; set; } = true;
    public AppAdOptions AdOptions { get; set; } = new();
    public bool AllowEndPointTracker { get; set; }
    public string? DeviceId { get; set; }
    public TimeSpan? EventWatcherInterval { get; set; } // set if you don't call State periodically
    public bool DisconnectOnDispose { get; set; }
    public LogServiceOptions LogServiceOptions { get; set; } = new();
    public bool AdjustForSystemBars { get; set; } = true;
    public bool AllowEndPointStrategy { get; set; }
    public object? CustomData { get; set; }
    public bool AllowRecommendUserReviewByServer { get; set; }
    public Uri? RemoteSettingsUrl { get; set; }
    public int? WebUiPort { get; set; }
    public string? WebUiHostName { get; set; }
}
