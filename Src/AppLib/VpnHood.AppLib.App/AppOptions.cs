using Ga4.Trackers;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Services.Ads;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Tunneling.Factory;

namespace VpnHood.AppLib;

public class AppOptions(string appId, string storageFolderName, bool isDebugMode)
{
    public static string BuildStorageFolderPath(string subFolder) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), subFolder);

    public string AppId => appId;
    public bool IsDebugMode => isDebugMode;
    public string StorageFolderPath { get; set; } = BuildStorageFolderPath(storageFolderName);
    public TimeSpan SessionTimeout { get; set; } = ClientOptions.Default.SessionTimeout;
    public ISocketFactory? SocketFactory { get; set; }
    public TimeSpan VersionCheckInterval { get; set; } = TimeSpan.FromHours(24);
    public string? UpdateInfoUrl { get; set; }
    public bool UseInternalLocationService { get; set; } = true;
    public bool UseExternalLocationService { get; set; } = true;
    public AppResource Resource { get; set; } = new();
    public string? Ga4MeasurementId { get; set; } = "G-4LE99XKZYE";
    public string? UiName { get; set; }
    public bool IsAddAccessKeySupported { get; set; } = true;
    public bool IsLocalNetworkSupported { get; set; }
    public string[] AccessKeys { get; set; } = [];
    public IAppUiProvider? UiProvider { get; set; }
    public IAppCultureProvider? CultureProvider { get; set; }
    public IAppUpdaterProvider? UpdaterProvider { get; set; }
    public IAppAccountProvider? AccountProvider { get; set; }
    public AppAdProviderItem[] AdProviderItems { get; set; } = [];
    public ITracker? Tracker { get; set; }
    public TimeSpan ReconnectTimeout { get; set; } = ClientOptions.Default.ReconnectTimeout;
    public TimeSpan AutoWaitTimeout { get; set; } = ClientOptions.Default.AutoWaitTimeout;
    public bool LogVerbose { get; set; }

    public bool? LogAnonymous { get; set; } =
        isDebugMode ? false : null; // it follows user's settings if it set to null

    public TimeSpan ServerQueryTimeout { get; set; } = ClientOptions.Default.ServerQueryTimeout;
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public bool SingleLineConsoleLog { get; set; } = true;
    public bool AutoDiagnose { get; set; } = true;
    public AppAdOptions AdOptions { get; set; } = new();
    public bool AllowEndPointTracker { get; set; }
    public string? DeviceId { get; set; }
    public string? LocalSpaHostName { get; set; }
    public TimeSpan CanExtendByRewardedAdThreshold { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan? EventWatcherInterval { get; set; } // set if you don't call State periodically
    public bool DisconnectOnDispose { get; set; }
}