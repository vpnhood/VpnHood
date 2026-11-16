using VpnHood.AppLogic.Api.App;
using VpnHood.AppLogic.Api.Sessions;

namespace VpnHood.AppUi.Avalonia.Classic.Views;

// What a connect is asked for: which server, which of its locations
// (null for the automatic one), on the premium or the free side, and with which plan. GoToHome is
// the default, as it is there: a connect started from a page shows on the home.
public sealed record ConnectRequest(
    Guid VpnProfileId,
    string? ServerLocation,
    bool IsPremium,
    ConnectPlanId PlanId,
    bool IsDiagnose = false,
    bool GoToHome = true);
