namespace VpnHood.AppUi.Hosting.Cli;

// Registering the instance with the OS and removing it, where the app does that itself rather than
// its package's installer (Windows: the service, its start rights, and later the PATH entry). Both
// print for a person and return only an exit code, as the instance controller's Show* calls do, and
// both ask for elevation when this process has none.
public interface IAppInstanceSetup
{
    // Again on a registered instance, the registration is brought back to what it should be.
    Task<int> Install(CancellationToken cancellationToken);
    Task<int> Uninstall(CancellationToken cancellationToken);
}
