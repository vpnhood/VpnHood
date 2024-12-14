using Ga4.Trackers;
using VpnHood.AppFramework.Abstractions;
using VpnHood.AppFramework.Services;
using VpnHood.AppFramework.Services.Accounts;

namespace VpnHood.AppFramework;

public class AppServices
{
    public required AppAccountService? AccountService { get; init; }
    public required AppAdService AdService { get; init; }
    public required IAppUpdaterProvider? UpdaterProvider { get; init; }
    public required IAppUiProvider UiProvider { get; init; }
    public required IAppCultureProvider CultureProvider { get; init; }
    public required ITracker? Tracker { get; set; }
}