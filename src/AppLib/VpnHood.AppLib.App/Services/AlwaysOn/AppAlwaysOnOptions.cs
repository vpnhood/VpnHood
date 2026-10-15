namespace VpnHood.AppLib.App.Services.AlwaysOn;

public class AppAlwaysOnOptions
{
    // The pause before each try after a failure to reach a working server, growing so that a server that
    // is down is never bombarded; the last one repeats. Each is up to a fifth shorter at random, so that
    // machines do not try in step.
    public IReadOnlyList<TimeSpan> RetryPauses { get; set; } = [
        TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10)
    ];

    // a session that held this long starts the pauses over
    public TimeSpan RetryPausesResetTime { get; set; } = TimeSpan.FromMinutes(10);
}
