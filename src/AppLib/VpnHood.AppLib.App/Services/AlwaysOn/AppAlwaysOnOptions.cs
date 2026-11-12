namespace VpnHood.AppLib.App.Services.AlwaysOn;

/// <summary>Provides how always on tries again after a failure (<see cref="AppOptions.AlwaysOnOptions"/>).</summary>
public class AppAlwaysOnOptions
{
    /// <summary>
    /// Gets or sets the pauses before each try after a failure to reach a working server, growing so a server that is
    /// down is never bombarded; the last repeats. The default is 30 seconds, then one, two, five and ten minutes.
    /// </summary>
    /// <remarks>
    /// It must hold at least one pause. Each pause is up to a fifth shorter at random, so devices do not try in step.
    /// </remarks>
    public IReadOnlyList<TimeSpan> RetryPauses { get; set; } = [
        TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10)
    ];

    /// <summary>
    /// Gets or sets how long a session must last for the pauses to start over. The default is ten minutes.
    /// </summary>
    public TimeSpan RetryPausesResetTime { get; set; } = TimeSpan.FromMinutes(10);
}
