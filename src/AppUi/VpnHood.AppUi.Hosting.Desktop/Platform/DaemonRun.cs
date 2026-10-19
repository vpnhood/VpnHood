namespace VpnHood.AppUi.Hosting.Desktop.Platform;

// The daemon's run, as the platform's service manager hosts it (IDesktopPlatform.HostDaemon). stopToken is
// cancelled when the manager stops the daemon, and quickCloseToken when the time the stop leaves the app's
// graceful close runs out, which then gives way to its quick one. It returns the process's exit code.
public delegate Task<int> DaemonRun(CancellationToken stopToken, CancellationToken quickCloseToken);
