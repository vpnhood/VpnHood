using VpnHood.Tunneling.Factory;

namespace VpnHood.Client.App;

public class AppOptions
{
    public string AppDataFolderPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnHood");
    public TimeSpan SessionTimeout { get; set; } = new ClientOptions().SessionTimeout;
    public SocketFactory? SocketFactory { get; set; }
    public TimeSpan VersionCheckInterval { get; set; } = TimeSpan.FromHours(24);
    public Uri? UpdateInfoUrl { get; set; }
    public bool LoadCountryIpGroups { get; set; } = true;
    public AppResource Resource { get; set; } = new();
    public string? AppGa4MeasurementId { get; set; } = "G-4LE99XKZYE";
    public string? UiName { get; set; }
    public bool IsAddAccessKeySupported { get; set; } = true;
    public string[] AccessKeys { get; set; } = [];
}