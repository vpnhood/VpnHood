using VpnHood.Client.App.Abstractions;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Client.App;

public class AppOptions
{
    public static string DefaultStorageFolderPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnHood");
    public string StorageFolderPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnHood");
    public TimeSpan SessionTimeout { get; set; } = new ClientOptions().SessionTimeout;
    public SocketFactory? SocketFactory { get; set; }
    public TimeSpan VersionCheckInterval { get; set; } = TimeSpan.FromHours(24);
    public Uri? UpdateInfoUrl { get; set; }
    public bool UseIpGroupManager { get; set; } = true;
    public bool UseExternalLocationService { get; set; } = true;
    public AppResource Resource { get; set; } = new();
    public string? AppGa4MeasurementId { get; set; } = "G-4LE99XKZYE";
    public string? UiName { get; set; }
    public bool IsAddAccessKeySupported { get; set; } = true;
    public string[] AccessKeys { get; set; } = [];
    public IAppUiService? UiService { get; set; }
    public IAppCultureService? CultureService { get; set; }
    public IAppUpdaterService? UpdaterService { get; set; }
    public IAppAccountService? AccountService { get; set; }
    public IAppAdService? AdService { get; set; }
}