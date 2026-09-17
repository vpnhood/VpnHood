using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Sessions;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Views;

// What a connect is asked for (the web UI's ConnectParams): which server, which of its locations
// (null for the automatic one), on the premium or the free side, and with which plan. GoToHome is
// the default, as it is there: a connect started from a page shows on the home.
public sealed record ConnectRequest(
    Guid ClientProfileId,
    string? ServerLocation,
    bool IsPremium,
    ConnectPlanId PlanId,
    bool IsDiagnose = false,
    bool GoToHome = true);
