namespace VpnHood.AppUi.Hosting.Cli;

// How this OS builds the app for the two commands that hold it: "daemon", the service, and "dev",
// a debugger's run with the window in one process.
public interface IAppDaemonHostFactory
{
    // The service, in its own storage. Throws, saying why, when this process may not be it.
    IAppDaemonHost CreateService();

    // A debugger's, in the storage it is handed, as whoever runs it.
    IAppDaemonHost CreateDev(string storagePath);
}
