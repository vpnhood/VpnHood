namespace VpnHood.AppUi.Hosting.Cli;

// The platform's half of the daemon command: the app, built the way this OS hosts a headless one -
// VpnHoodAppLinux on Linux, with its single-instance socket and command file - and whatever must
// happen around it. The shared half (DaemonCommand) binds the API, publishes the address and waits.
//
// Constructing one IS starting the app; a platform that cannot - not root, another instance up -
// throws from its constructor with the sentence a person should read. Disposing one ends it.
public interface IAppDaemonHost : IDisposable
{
    // What a run does before its API is offered: on Linux, delete the adapter a previous run may
    // have left, since its route may still be active.
    Task Prepare(CancellationToken cancellationToken);
}
