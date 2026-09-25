using System.ServiceProcess;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Cli.Windows.Internal;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Cli.Windows;

// The instance on Windows is a service named after the install, under the service control manager.
// Anyone signed in may start it - the right "service install" grants - so the window brings a stopped
// service back without a prompt. Stopping it is an administrator's, so without elevation the command
// runs itself again as one, as sudo does on Linux. A service not registered yet is registered on the
// way to starting it, which is how a window from a zip, or a fork's own packager, gets one; a
// registered one is never registered again by a start - only an update moves the service (hosting
// plan §5.5).
public class WindowsInstanceController(WindowsCliPaths paths, WindowsServiceSetup setup) : IAppInstanceController
{
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public string NotRunningHint =>
        $"The {paths.InstanceName} service is not running. Start it with: {paths.CommandName} service start";

    // One that is starting counts: a caller waiting for its address must not be told it died.
    public Task<bool> IsRunning(CancellationToken cancellationToken)
    {
        return Task.FromResult(GetStatus() is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending);
    }

    public async Task<int> Start(CancellationToken cancellationToken)
    {
        if (!setup.IsRegistered) {
            var installCode = await setup.Install(cancellationToken).Vhc();
            if (installCode != 0)
                return installCode;
        }

        try {
            using var controller = new ServiceController(paths.InstanceName);

            // a stop still under way is let finish; the manager refuses a start meanwhile
            if (controller.Status == ServiceControllerStatus.StopPending)
                await WaitForStatus(controller, ServiceControllerStatus.Stopped, cancellationToken).Vhc();

            if (controller.Status == ServiceControllerStatus.Stopped)
                controller.Start();

            await WaitForStatus(controller, ServiceControllerStatus.Running, cancellationToken).Vhc();
            return 0;
        }
        catch (InvalidOperationException ex) {
            await Console.Error.WriteLineAsync(ex.InnerException?.Message ?? ex.Message).Vhc();
            return 1;
        }
    }

    public async Task<int> Stop(CancellationToken cancellationToken)
    {
        if (!WindowsElevation.IsElevated)
            return await WindowsElevation.Run(paths.ExecutablePath, ["service", "stop"], cancellationToken).Vhc();

        if (GetStatus() is not { } status) {
            Console.WriteLine($"{paths.InstanceName} is not registered.");
            return 0;
        }

        using var controller = new ServiceController(paths.InstanceName);
        if (status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending))
            controller.Stop();

        await WaitForStatus(controller, ServiceControllerStatus.Stopped, cancellationToken).Vhc();
        return 0;
    }

    public async Task<int> Restart(CancellationToken cancellationToken)
    {
        if (!WindowsElevation.IsElevated)
            return await WindowsElevation.Run(paths.ExecutablePath, ["service", "restart"], cancellationToken).Vhc();

        var stopCode = await Stop(cancellationToken).Vhc();
        return stopCode != 0 ? stopCode : await Start(cancellationToken).Vhc();
    }

    // What "systemctl status" would say, in its exit code too: 0 running, 3 not.
    public Task<int> ShowStatus(CancellationToken cancellationToken)
    {
        if (GetStatus() is not { } status) {
            Console.WriteLine($"{paths.InstanceName} is not registered. Register it with: {paths.CommandName} service install");
            return Task.FromResult(3);
        }

        using var controller = new ServiceController(paths.InstanceName);
        Console.WriteLine($"{controller.ServiceName} - {controller.DisplayName}");
        Console.WriteLine($"  Status:  {status}");
        Console.WriteLine($"  Starts:  {controller.StartType}");
        if (DaemonInfo.Read(((IAppCliPaths)paths).DaemonInfoFilePath) is { } daemonInfo) {
            Console.WriteLine($"  Process: {daemonInfo.ProcessId}");
            Console.WriteLine($"  Version: {daemonInfo.Version}");
            Console.WriteLine($"  Address: {daemonInfo.ApiUrl}");
        }

        Console.WriteLine($"  Storage: {paths.StoragePath}");
        return Task.FromResult(status == ServiceControllerStatus.Running ? 0 : 3);
    }

    // The app's own log in the service's storage, which everyone may read: its last lines, and with
    // follow what comes after as it comes, until Ctrl+C.
    public async Task<int> ShowLog(bool follow, int lines, CancellationToken cancellationToken)
    {
        var logFilePath = Path.Combine(paths.StoragePath, VpnHoodApp.FileNameLog);
        if (!File.Exists(logFilePath)) {
            await Console.Error.WriteLineAsync($"There is no log yet: {logFilePath}").Vhc();
            return 1;
        }

        // the service holds it open for writing, and may replace it
        await using var stream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        var tail = new Queue<string>();
        while (await reader.ReadLineAsync(cancellationToken).Vhc() is { } line) {
            if (tail.Count == lines)
                tail.Dequeue();
            tail.Enqueue(line);
        }

        foreach (var line in tail)
            Console.WriteLine(line);

        if (!follow)
            return 0;

        try {
            while (true) {
                if (await reader.ReadLineAsync(cancellationToken).Vhc() is { } line)
                    Console.WriteLine(line);
                else
                    await Task.Delay(PollInterval, cancellationToken).Vhc();
            }
        }
        catch (OperationCanceledException) {
            return 0;
        }
    }

    // Null when no such service is registered.
    private ServiceControllerStatus? GetStatus()
    {
        using var controller = new ServiceController(paths.InstanceName);
        try {
            return controller.Status;
        }
        catch (InvalidOperationException) {
            return null;
        }
    }

    // A start that ends in Stopped has failed, and is said so at once rather than after the timeout.
    private async Task WaitForStatus(ServiceController controller, ServiceControllerStatus status,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StatusTimeout);
        try {
            while (true) {
                controller.Refresh();
                if (controller.Status == status)
                    return;

                if (status == ServiceControllerStatus.Running && controller.Status == ServiceControllerStatus.Stopped)
                    throw new InvalidOperationException(
                        $"{paths.InstanceName} stopped as it started. See: {paths.CommandName} service log");

                await Task.Delay(PollInterval, timeout.Token).Vhc();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            throw new System.TimeoutException($"{paths.InstanceName} did not reach {status} in time.");
        }
    }
}
