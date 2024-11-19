using Ga4.Trackers;
using VpnHood.Client.App.Abstractions;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Client.App;

public class AppOptions(string appId)
{
    public static string BuildStorageFolderPath(string appId) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appId);

    public string AppId { get; init; } = appId;
    public string StorageFolderPath { get; set; } = BuildStorageFolderPath(appId);
    public TimeSpan SessionTimeout { get; set; } = ClientOptions.Default.SessionTimeout;
    public SocketFactory? SocketFactory { get; set; }
    public TimeSpan VersionCheckInterval { get; set; } = TimeSpan.FromHours(24);
    public Uri? UpdateInfoUrl { get; set; }
    public bool UseInternalLocationService { get; set; } = true;
    public bool UseExternalLocationService { get; set; } = true;
    public AppResource Resource { get; set; } = new();
    public string? AppGa4MeasurementId { get; set; } = "G-4LE99XKZYE";
    public string? UiName { get; set; }
    public bool IsAddAccessKeySupported { get; set; } = true;
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
    public bool? LogAnonymous { get; set; }
    public TimeSpan ServerQueryTimeout { get; set; } = ClientOptions.Default.ServerQueryTimeout;
    public bool SingleLineConsoleLog { get; set; } = true;
    public bool AutoDiagnose { get; set; } = true;
    public AppAdOptions AdOptions { get; set; } = new();
    public bool AllowEndPointTracker { get; set; }
    public string? DeviceId { get; set; }
    public string? LocalSpaHostName { get; set; }
}