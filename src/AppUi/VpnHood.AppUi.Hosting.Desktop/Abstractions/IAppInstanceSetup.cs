namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

// Registering the instance with the OS and removing it, where the app does that itself rather than
// its package's installer (Windows: the service, its start rights, and later the PATH entry). Both
// print for a person and return only an exit code, as the instance controller's Show* calls do, and
// both ask for elevation when this process has none.
public interface IAppInstanceSetup
{
    // Whether the instance is registered with the OS. A start nobody asked for - the tray's, at
    // sign-in - registers nothing, since that asks for elevation.
    bool IsRegistered { get; }

    // Again on a registered instance, the registration is brought back to what it should be.
    Task<int> Install(CancellationToken cancellationToken);
    Task<int> Uninstall(CancellationToken cancellationToken);
}
