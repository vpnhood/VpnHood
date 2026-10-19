namespace VpnHood.AppUi.Hosting.Desktop.Platform;

// The app, as this OS hosts a headless one (IAppDaemonHostFactory). Creating one IS starting it;
// disposing one ends it: asynchronously, so a connected tunnel is taken down before the process goes,
// or at once, which releases the adapter with no goodbye, for a stop whose time has run out.
public interface IAppDaemonHost : IAsyncDisposable, IDisposable;
