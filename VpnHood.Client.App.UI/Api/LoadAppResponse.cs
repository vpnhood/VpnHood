using VpnHood.Client.App.Settings;

namespace VpnHood.Client.App.UI.Api;

public class LoadAppResponse
{
    public AppFeatures? Features { get; set; }
    public AppSettings? Settings { get; set; }
    public AppState? State { get; set; }
    public ClientProfileItem[]? ClientProfileItems { get; set; }
}