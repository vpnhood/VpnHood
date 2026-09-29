namespace VpnHood.AppUi.Hosting.Cli.Abstractions;

// The app, as this OS hosts a headless one (IAppDaemonHostFactory). Creating one IS starting it;
// disposing one ends it, asynchronously, so a connected tunnel is taken down before the process goes.
public interface IAppDaemonHost : IAsyncDisposable;
