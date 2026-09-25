namespace VpnHood.AppUi.Hosting.Cli;

// The platform's half of the daemon command: the app, built the way this OS hosts a headless one -
// VpnHoodLinuxApp on Linux, with its single-instance lock and its cleanup of what a previous run
// left, all inside its start. The shared half (DaemonCommand) binds the API, publishes the address
// and waits.
//
// Constructing one IS starting the app; a platform that cannot - not root, another instance up -
// throws from its constructor with the sentence a person should read. Disposing one ends it, and
// asynchronously, so a connected tunnel is taken down before the process goes.
public interface IAppDaemonHost : IAsyncDisposable;
