using VpnHood.AppLib.Api.App;

namespace VpnHood.AppLib.App;

public class ConnectOptions
{
    public Guid? ClientProfileId { get; init; }
    public ConnectPlanId PlanId { get; init; } = ConnectPlanId.Normal;
    public string? ServerLocation { get; init; }
    public bool Diagnose { get; init; }
    public string? UserAgent { get; init; }
}