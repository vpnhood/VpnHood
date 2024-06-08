using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App;

public class AppServices
{
    public required IAppAccountService? AccountService { get; init; }
    public required IAppUpdaterService? UpdaterService { get; init; }
    public required IAppAdService[]? AdService { get; init; } = [];
    public required IAppUiService UiService { get; init; }
    public required IAppCultureService AppCultureService { get; init;}
}