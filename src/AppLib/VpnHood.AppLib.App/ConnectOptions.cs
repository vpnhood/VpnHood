using VpnHood.AppLib.Api.App;

namespace VpnHood.AppLib.App;

/// <summary>Provides the options of a connect (<see cref="VpnHoodApp.Connect"/>).</summary>
public class ConnectOptions
{
    /// <summary>
    /// Gets the ID of the VPN profile to connect with, or <see langword="null"/> for the one the settings select
    /// (<see cref="VpnHood.AppLib.Api.Settings.UserSettings.VpnProfileId"/>).
    /// </summary>
    public Guid? VpnProfileId { get; init; }

    /// <summary>
    /// Gets the plan to connect on, which the server may grant or refuse. The default is
    /// <see cref="ConnectPlanId.Normal"/>.
    /// </summary>
    public ConnectPlanId PlanId { get; init; } = ConnectPlanId.Normal;

    /// <summary>
    /// Gets the server location to connect to, or <see langword="null"/> for the profile's selected one. The connect
    /// selects it on the profile and the profile in the settings.
    /// </summary>
    public string? ServerLocation { get; init; }

    /// <summary>
    /// Gets a value indicating whether the connect diagnoses: it checks the server's endpoints and the network before
    /// it starts and the tunnel after, and may take three times as long (<see cref="AppOptions.ConnectTimeout"/>).
    /// </summary>
    public bool Diagnose { get; init; }

    /// <summary>
    /// Gets a value indicating whether the connect's caller tries again if it fails, as the Windows Store edition's
    /// does after a wake: its failure is not shown, as the next try may connect, and does not set off the automatic
    /// check of the network (<see cref="AppOptions.AutoDiagnose"/>).
    /// </summary>
    public bool IsRetried { get; init; }

    // always on's own connect (AppAlwaysOnService), which the VPN service refuses where the plan does not
    // allow always on (ClientOptions.IsAlwaysOn); any other is a person's
    internal bool IsAlwaysOn { get; init; }
}