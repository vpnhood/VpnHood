using System.Diagnostics;
using System.ServiceProcess;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;
using VpnHood.AppUi.Hosting.Desktop.WindowsTun.Utils;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsTun;

// "service install" and "service uninstall": the service registered at this executable - automatic,
// LocalSystem, restarted on failure, startable by anyone signed in - and removed again; in an
// installer's layout, at the install root's launcher, with the root on the machine PATH so the command
// is typed by its name (WindowsDesktopPaths.InstallRootPath), and an access key's file and link opening in the
// window, which imports the key (DesktopInitParams.AccessKeyFileExtensions, AccessKeySchemes). The installer runs
// them; a window that finds no service runs install itself (WindowsInstanceController.Start).
// Without elevation each runs itself again as an administrator, behind one UAC prompt.
//
// Only a copy that nobody else can change is registered, since the service runs it as SYSTEM
// (WindowsInstallFolder). The folder is checked before the prompt, since reading access lists needs
// no elevation, so the reason reaches whoever asked: the elevated copy's output reaches nobody. The
// elevated copy checks again, as the boundary, and answers with an exit code of its own.
public class WindowsServiceSetup(
    WindowsDesktopPaths paths,
    string appName,
    IReadOnlyList<string> accessKeyFileExtensions,
    IReadOnlyList<string> accessKeySchemes)
    : IAppInstanceSetup
{
    private const int UnsafeFolderExitCode = 4;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(60);

    public bool IsRegistered => ServiceRegistration.IsRegistered(paths.InstanceName);

    // the file type the access key's extensions name, one per instance
    private string AccessKeyProgId => paths.InstanceName + ".AccessKey";

    // what an access key's file or link runs: the root's launcher, which an update never moves
    private string? AccessKeyOpenCommand =>
        paths.InstalledLauncherPath is { } launcherPath ? $"\"{launcherPath}\" ui --access-key \"%1\"" : null;

    public async Task<int> Install(CancellationToken cancellationToken)
    {
        if (FindUnsafeFolder() is { } reason) {
            await Console.Error.WriteLineAsync(reason).Vhc();
            return UnsafeFolderExitCode;
        }

        if (!WindowsElevation.IsElevated)
            return await WindowsElevation.Run(paths.ExecutablePath, ["service", "install"], cancellationToken).Vhc();

        ServiceRegistration.Register(paths.InstanceName, appName,
            description: $"Keeps the {appName} VPN. Its window, its tray and the {paths.CommandName} commands drive it.",
            commandLine: $"\"{paths.ExecutablePath}\" daemon");

        // The window writes under the service's name too, as the person, who may write to a source but
        // not create one; the service's own first entry would, but a window that could not start it
        // has to be heard all the same.
        if (!EventLog.SourceExists(paths.InstanceName))
            EventLog.CreateEventSource(paths.InstanceName, "Application");

        Console.WriteLine($"{paths.InstanceName} is registered, and starts with Windows.");
        if (paths.InstallRootPath is { } installRoot && WindowsMachinePath.Add(installRoot))
            Console.WriteLine($"{installRoot} is on the PATH: a terminal opened from now on runs {paths.CommandName} by its name.");

        if (paths.InstalledLauncherPath is { } launcherPath && AccessKeyOpenCommand is { } openCommand) {
            if (accessKeyFileExtensions.Count > 0) {
                WindowsFileAssociation.Add(AccessKeyProgId, $"{appName} access key", appName, launcherPath, openCommand,
                    accessKeyFileExtensions);
                Console.WriteLine($"{string.Join(", ", accessKeyFileExtensions)} files open in {appName}.");
            }

            foreach (var scheme in accessKeySchemes)
                Console.WriteLine(WindowsUrlScheme.Add(scheme, $"{appName} access key", appName, launcherPath, openCommand)
                    ? $"{scheme}: links open in {appName}."
                    : $"{scheme}: links stay with the app that already opens them.");
        }

        return 0;
    }

    // Install for the window (WindowsInstanceController.Start), which shows why it could not: it
    // throws, saying so, where a command prints.
    internal async Task Register(CancellationToken cancellationToken)
    {
        if (FindUnsafeFolder() is { } reason)
            throw new InvalidOperationException(reason);

        var exitCode = await Install(cancellationToken).Vhc();
        if (exitCode == UnsafeFolderExitCode)
            throw new InvalidOperationException(
                $"{paths.InstanceName} is not registered: checked again as an administrator, its folder is one others can change.");

        if (exitCode != 0)
            throw new InvalidOperationException(
                $"Could not register {paths.InstanceName}. To see why, run \"{paths.CommandName} service install\" as an administrator.");
    }

    // Why this copy may not be the service, as a person reads it; null when it may.
    private string? FindUnsafeFolder()
    {
        // the folder of what the service runs: the install root, whose "current" names this version
        var folder = Path.GetDirectoryName(paths.ExecutablePath) ?? AppContext.BaseDirectory;
        return WindowsInstallFolder.FindProblem(folder) is { } problem
            ? $"{paths.InstanceName} runs as SYSTEM, so it is not registered from a folder others can change: {problem}. " +
              "Copy it under Program Files, or into a folder only administrators can change."
            : null;
    }

    public async Task<int> Uninstall(CancellationToken cancellationToken)
    {
        if (!WindowsElevation.IsElevated)
            return await WindowsElevation.Run(paths.ExecutablePath, ["service", "uninstall"], cancellationToken).Vhc();

        // all of what install added, whatever is left of it
        if (paths.InstallRootPath is { } installRoot && WindowsMachinePath.Remove(installRoot))
            Console.WriteLine($"{installRoot} is off the PATH.");

        if (WindowsFileAssociation.Remove(AccessKeyProgId, accessKeyFileExtensions))
            Console.WriteLine($"{string.Join(", ", accessKeyFileExtensions)} files no longer open in {appName}.");

        if (AccessKeyOpenCommand is { } openCommand)
            foreach (var scheme in accessKeySchemes.Where(scheme => WindowsUrlScheme.Remove(scheme, openCommand)))
                Console.WriteLine($"{scheme}: links no longer open in {appName}.");

        if (!IsRegistered) {
            Console.WriteLine($"{paths.InstanceName} is not registered.");
            return 0;
        }

        // the tunnel comes down with the service's own stop, before it is removed
        using var controller = new ServiceController(paths.InstanceName);
        if (controller.Status != ServiceControllerStatus.Stopped) {
            if (controller.Status != ServiceControllerStatus.StopPending)
                controller.Stop();

            controller.WaitForStatus(ServiceControllerStatus.Stopped, StopTimeout);
        }

        ServiceRegistration.Unregister(paths.InstanceName);
        Console.WriteLine($"{paths.InstanceName} is removed.");
        return 0;
    }
}
