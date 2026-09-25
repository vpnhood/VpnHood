using System.Diagnostics;
using System.ServiceProcess;
using VpnHood.AppUi.Hosting.Cli.Windows.Internal;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Cli.Windows;

// "service install" and "service uninstall": the service registered at this executable - automatic,
// LocalSystem, restarted on failure, startable by anyone signed in - and removed again. The installer
// runs them; a window that finds no service runs install itself (WindowsInstanceController.Start).
// Without elevation each runs itself again as an administrator, behind one UAC prompt.
public class WindowsServiceSetup(WindowsCliPaths paths) : IAppInstanceSetup
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(60);

    public bool IsRegistered => ServiceRegistration.IsRegistered(paths.InstanceName);

    public Task<int> Install(CancellationToken cancellationToken)
    {
        if (!WindowsElevation.IsElevated)
            return WindowsElevation.Run(paths.ExecutablePath, ["service", "install"], cancellationToken);

        var productName = FileVersionInfo.GetVersionInfo(paths.ExecutablePath).ProductName;
        var displayName = string.IsNullOrWhiteSpace(productName) ? paths.InstanceName : productName;
        ServiceRegistration.Register(paths.InstanceName, displayName,
            description: $"Keeps the {displayName} VPN. Its window, its tray and the {paths.CommandName} commands drive it.",
            commandLine: $"\"{paths.ExecutablePath}\" daemon");

        Console.WriteLine($"{paths.InstanceName} is registered, and starts with Windows.");
        return Task.FromResult(0);
    }

    public async Task<int> Uninstall(CancellationToken cancellationToken)
    {
        if (!WindowsElevation.IsElevated)
            return await WindowsElevation.Run(paths.ExecutablePath, ["service", "uninstall"], cancellationToken).Vhc();

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
