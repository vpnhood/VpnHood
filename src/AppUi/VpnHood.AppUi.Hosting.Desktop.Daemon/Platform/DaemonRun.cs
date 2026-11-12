namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;

/// <summary>Represents the service's run, as the platform's service manager hosts it.</summary>
/// <param name="stopToken">The token canceled when the manager stops the service.</param>
/// <param name="quickCloseToken">
/// The token canceled when the time the stop leaves the app's graceful close runs out, which then gives way to its quick
/// one.
/// </param>
/// <returns>A task whose result is the process's exit code.</returns>
public delegate Task<int> DaemonRun(CancellationToken stopToken, CancellationToken quickCloseToken);
