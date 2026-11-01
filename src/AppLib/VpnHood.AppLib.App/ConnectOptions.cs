using VpnHood.AppLib.Api.App;

namespace VpnHood.AppLib.App;

public class ConnectOptions
{
    public Guid? VpnProfileId { get; init; }
    public ConnectPlanId PlanId { get; init; } = ConnectPlanId.Normal;
    public string? ServerLocation { get; init; }
    public bool Diagnose { get; init; }

    // a try its caller makes again if it fails (the Store edition's after a wake, WindowsVpnPluginSleepReconnect),
    // whose failure the person does not see, as the next try may connect: the try that gives up shows its own
    public bool IsRetried { get; init; }

    // always on's own connect (AppAlwaysOnService), which the VPN service refuses where the plan does not
    // allow always on (ClientOptions.IsAlwaysOn); any other is a person's
    internal bool IsAlwaysOn { get; init; }
}