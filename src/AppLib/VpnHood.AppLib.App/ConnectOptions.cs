using VpnHood.AppLib.Api.App;

namespace VpnHood.AppLib.App;

public class ConnectOptions
{
    public Guid? VpnProfileId { get; init; }
    public ConnectPlanId PlanId { get; init; } = ConnectPlanId.Normal;
    public string? ServerLocation { get; init; }
    public bool Diagnose { get; init; }

    // always on's own connect (AppAlwaysOnService), which the VPN service refuses where the plan does not
    // allow always on (ClientOptions.IsAlwaysOn); any other is a person's
    internal bool IsAlwaysOn { get; init; }
}