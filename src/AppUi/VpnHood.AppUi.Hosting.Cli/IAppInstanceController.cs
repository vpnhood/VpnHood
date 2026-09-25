namespace VpnHood.AppUi.Hosting.Cli;

// The running instance - the one process on this machine that holds the app - as the commands
// start, stop and look at it. Deliberately not "the service" in its members: on Linux it is a systemd
// unit, on Windows a service under the service control manager, in a Store build whatever the package
// launches, and the commands must not care which. Each platform answers these in its own terms.
//
// The Show* calls print for a person and return only an exit code - they are systemctl status and
// journalctl on Linux, and whatever reads the same way elsewhere.
public interface IAppInstanceController
{
    // The sentence a person reads when nothing is running: what it is, and how to start it.
    string NotRunningHint { get; }

    Task<bool> IsRunning(CancellationToken cancellationToken);
    Task<int> Start(CancellationToken cancellationToken);
    Task<int> Stop(CancellationToken cancellationToken);
    Task<int> Restart(CancellationToken cancellationToken);
    Task<int> ShowStatus(CancellationToken cancellationToken);
    Task<int> ShowLog(bool follow, int lines, CancellationToken cancellationToken);
}
