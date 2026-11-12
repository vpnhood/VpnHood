using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Updaters;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsTun;

/// <summary>
/// Provides the app's update on Windows, for an install from the website: it hands the package to the install's own
/// updater, <c>vhupdate.ps1</c> in its root, which is the automatic update too.
/// </summary>
/// <remarks>
/// The task's runs of the updater check the feed; an install here hands it the downloaded package, whose own version it
/// checks, then runs the setup silently, which stops this service, closes the windows and trays that run from the
/// install, puts the new version in and asks Windows to start it, and the new service tries to open those windows and
/// trays again.
/// Here the updater runs as a child of this service; its output goes to <c>update.log</c> beside it.
/// </remarks>
public sealed class WindowsAppUpdaterProvider : DesktopAppUpdaterProvider
{
    private const string UpdaterFileName = "vhupdate.ps1";
    private readonly string _installRootPath;

    private WindowsAppUpdaterProvider(string installRootPath, string instanceName, Uri updateInfoUrl,
        Version appVersion)
        : base(Path.Combine(installRootPath, "updates"), Path.Combine(installRootPath, MissedFileName), updateInfoUrl,
            appVersion)
    {
        _installRootPath = installRootPath;
        TaskName = $"{instanceName}Updater";
    }

    /// <summary>
    /// Creates the updater of an install the setup made, whose version's folder holds the setup's <c>publish.json</c>.
    /// </summary>
    /// <param name="context">The app's options context, with its app ID and version.</param>
    /// <returns>
    /// The updater, or <see langword="null"/> where the install has no updater script or the version's folder no
    /// <c>publish.json</c>, as in a debugger's build.
    /// </returns>
    /// <exception cref="System.IO.InvalidDataException">The <c>UpdateInfoUrl</c> property in <c>publish.json</c> is null.</exception>
    /// <exception cref="InvalidOperationException">The entry assembly has no name, so the install has none.</exception>
    public static WindowsAppUpdaterProvider? TryCreate(AppOptionsContext context)
    {
        return TryCreate(new WindowsDesktopPaths(context.AppId), context.AppVersion);
    }

    private static WindowsAppUpdaterProvider? TryCreate(WindowsDesktopPaths paths, Version appVersion)
    {
        if (paths.InstallRootPath is not { } installRootPath ||
            !File.Exists(Path.Combine(installRootPath, UpdaterFileName)) ||
            ReadUpdateInfoUrl(Path.Combine(AppContext.BaseDirectory, "publish.json")) is not { } updateInfoUrl)
            return null;

        return new WindowsAppUpdaterProvider(installRootPath, paths.InstanceName, updateInfoUrl, appVersion);
    }

    private string TaskName { get; }
    private static string SchTasksPath => Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    /// <summary>Gets whether the updater's task is on, as the setup left it or the person set it.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is <see langword="true"/> unless the task is disabled; <see langword="false"/> where the query
    /// fails.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="InvalidOperationException">schtasks could not be run.</exception>
    public override async Task<bool> IsAutomaticUpdateOn(CancellationToken cancellationToken)
    {
        var result = await RunTool(SchTasksPath, ["/Query", "/TN", TaskName, "/XML"], cancellationToken).Vhc();
        if (result.ExitCode != 0)
            return false;

        var enabled = XDocument.Parse(result.Output).Root?
            .Element(XName.Get("Settings", "http://schemas.microsoft.com/windows/2004/02/mit/task"))?
            .Element(XName.Get("Enabled", "http://schemas.microsoft.com/windows/2004/02/mit/task"))?.Value;
        return enabled != "false";
    }

    /// <summary>Turns the updater's task on or off, as the setup does.</summary>
    /// <param name="isOn"><see langword="true"/> to turn it on.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// Turned on, it asks for one run at once, a check now, which a run under way ignores; where that request fails, it is
    /// logged, and the task stays on.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="InvalidOperationException">schtasks could not be run, or could not enable or disable the task.</exception>
    public override async Task SetAutomaticUpdate(bool isOn, CancellationToken cancellationToken)
    {
        var result = await RunTool(SchTasksPath, ["/Change", "/TN", TaskName, isOn ? "/ENABLE" : "/DISABLE"],
            cancellationToken).Vhc();
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"schtasks could not {(isOn ? "enable" : "disable")} the {TaskName} task. {result.Error.Trim()}");

        if (!isOn)
            return;

        // the switch is on either way; only the check waits for the task's own triggers
        try {
            await RunUpdater(cancellationToken).Vhc();
        }
        catch (InvalidOperationException ex) {
            VhLogger.Instance.LogWarning(ex, "Could not run the updater's task at once.");
        }
    }

    /// <summary>Runs the updater's task now; its triggers keep their times, and a run under way ignores it.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the run is asked for.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="InvalidOperationException">schtasks could not be run, or could not run the task.</exception>
    protected override async Task RunUpdater(CancellationToken cancellationToken)
    {
        var result = await RunTool(SchTasksPath, ["/Run", "/TN", TaskName], cancellationToken).Vhc();
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"schtasks could not run the {TaskName} task. {result.Error.Trim()}");
    }

    /// <summary>Runs the install's updater on a package, and waits for it.</summary>
    /// <param name="packagePath">The package.</param>
    /// <returns>
    /// A task whose result is why the update installed nothing: the update log's last line that is not blank, or the
    /// updater's exit code. The setup stops this service before it puts anything in, so an updater that ends first installed
    /// nothing.
    /// </returns>
    /// <exception cref="InvalidOperationException">The updater could not be started.</exception>
    protected override async Task<string?> RunInstaller(string packagePath)
    {
        var startInfo = new ProcessStartInfo {
            FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] {
                     "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                     "-File", Path.Combine(_installRootPath, UpdaterFileName), "-packageFile", packagePath
                 })
            startInfo.ArgumentList.Add(argument);

        VhLogger.Instance.LogInformation("Installing the update. Package: {Package}", Path.GetFileName(packagePath));
        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Could not run the updater.");
        await process.WaitForExitAsync().Vhc();
        VhLogger.Instance.LogInformation("The updater ended. ExitCode: {ExitCode}", process.ExitCode);

        // the setup stops this service before it puts anything in, so an updater that ends first installed nothing
        return ReadLastLogLine() ?? $"The updater ended with code {process.ExitCode}.";
    }

    private string? ReadLastLogLine()
    {
        var logPath = Path.Combine(_installRootPath, "update.log");
        return File.Exists(logPath)
            ? File.ReadLines(logPath).LastOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim()
            : null;
    }
}
