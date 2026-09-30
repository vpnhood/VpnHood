using System.Diagnostics.Eventing.Reader;
using System.ServiceProcess;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Cli.Abstractions;
using VpnHood.AppUi.Hosting.Cli.Channel;
using VpnHood.AppUi.Hosting.Cli.Windows.Utils;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Cli.Windows;

// The instance on Windows is a service named after the install, under the service control manager.
// Anyone signed in may start it - the right "service install" grants - so the window brings a stopped
// service back without a prompt. Stopping it is an administrator's, so without elevation the command
// runs itself again as one, as sudo does on Linux. A service not registered yet is registered on the
// way to starting it, which is how a window from a zip, or a fork's own packager, gets one; a
// registered one is never registered again by a start - only an update moves the service (hosting
// plan §5.5).
public class WindowsInstanceController(WindowsCliPaths paths, WindowsServiceSetup setup, IDaemonChannel channel)
    : IAppInstanceController
{
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private const int EventLogEntryCount = 10;

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
    public async Task<int> ShowStatus(CancellationToken cancellationToken)
    {
        if (GetStatus() is not { } status) {
            Console.WriteLine($"{paths.InstanceName} is not registered. Register it with: {paths.CommandName} service install");
            return 3;
        }

        using var controller = new ServiceController(paths.InstanceName);
        Console.WriteLine($"{controller.ServiceName} - {controller.DisplayName}");
        Console.WriteLine($"  Status:  {status}");
        Console.WriteLine($"  Starts:  {controller.StartType}");
        if (status == ServiceControllerStatus.Running)
            await ShowChannel(cancellationToken).Vhc();

        Console.WriteLine($"  Storage: {paths.StoragePath}");
        return status == ServiceControllerStatus.Running ? 0 : 3;
    }

    // What the service says of itself, to an administrator alone; or why its channel did not answer.
    private async Task ShowChannel(CancellationToken cancellationToken)
    {
        try {
            var answer = await DaemonChannelClient.AskOnce(channel, cancellationToken).Vhc();
            if (answer.Refusal != null) {
                Console.WriteLine($"  Access:  {answer.Refusal}");
                return;
            }

            Console.WriteLine($"  Process: {answer.ProcessId}");
            Console.WriteLine($"  Version: {answer.Version}");
            Console.WriteLine($"  Address: {answer.ApiUrl?.GetLeftPart(UriPartial.Authority)}"); // never the token
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
            Console.WriteLine($"  Channel: {ex.Message}");
        }
    }

    // A start that fails before the app opens its log - over the storage or the lock - writes only to
    // the Application log, which a signed-in administrator may read unelevated: its latest entries under
    // the service's name. An elevated terminal may open the storage too, so it gets the app's own log.
    // Nothing is followed: what comes next comes from a service that answers.
    public async Task<int> ShowOfflineLog(bool follow, int lines, CancellationToken cancellationToken)
    {
        var entries = ReadEventLog(paths.InstanceName, EventLogEntryCount);
        foreach (var entry in entries)
            Console.WriteLine($"{entry.TimeCreated:yyyy-MM-dd HH:mm:ss} {entry.Level}: {entry.Message}");

        if (entries.Count == 0)
            Console.WriteLine($"The Application log holds nothing from {paths.InstanceName}.");

        if (!WindowsElevation.IsElevated)
            return 0;

        var logFilePath = Path.Combine(paths.StoragePath, VpnHoodApp.FileNameLog);
        if (!File.Exists(logFilePath))
            return 0;

        // the service may hold it open for writing, and may replace it
        await using var stream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var tail = new Queue<string>();
        while (await reader.ReadLineAsync(cancellationToken).Vhc() is { } line) {
            if (tail.Count == lines)
                tail.Dequeue();
            tail.Enqueue(line);
        }

        Console.WriteLine($"--- {logFilePath}");
        foreach (var line in tail)
            Console.WriteLine(line);

        return 0;
    }

    // The service's latest entries in the Application log, the oldest first.
    private static IReadOnlyList<(DateTime? TimeCreated, string Level, string Message)> ReadEventLog(
        string source, int count)
    {
        var query = new EventLogQuery("Application", PathType.LogName, $"*[System[Provider[@Name='{source}']]]") {
            ReverseDirection = true
        };

        using var reader = new EventLogReader(query);
        var entries = new List<(DateTime? TimeCreated, string Level, string Message)>();
        while (entries.Count < count) {
            using var record = reader.ReadEvent();
            if (record == null)
                break;

            var level = record.Level switch { 1 => "Critical", 2 => "Error", 3 => "Warning", _ => "Information" };
            entries.Add((record.TimeCreated, level, Describe(record)));
        }

        entries.Reverse();
        return entries;
    }

    // The message as the Event Viewer shows it, or its raw strings where the source's message file is gone.
    private static string Describe(EventRecord record)
    {
        try {
            if (record.FormatDescription() is { } description)
                return description;
        }
        catch (EventLogException) {
            // the raw strings below say it all the same
        }

        return string.Join(" ", record.Properties.Select(x => x.Value));
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
