using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;

namespace VpnHood.Client.App;

public class AppServices
{
    public IAppAccountService? AccountService { get; set; }
    public IAppUpdaterService? UpdaterService { get; set; }
    public IAppUiService? UiService { get; set; }
    public IAppAdService? AdService { get; set; }
    public required ICultureService CultureService { get; init;}
}