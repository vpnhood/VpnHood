using Ga4.Trackers;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Services.Accounts;
using VpnHood.AppLib.Services.Updaters;

namespace VpnHood.AppLib;

public class AppServices
{
    public required AppAccountService? AccountService { get; init; }
    public required AppUpdaterService? UpdaterService { get; init; }
    public required IAppUiProvider UiProvider { get; init; }
    public required IAppCultureProvider CultureProvider { get; init; }
    public required IAppUserReviewProvider? UserReviewProvider { get; init; }
    public required ITracker Tracker { get; set; }
}