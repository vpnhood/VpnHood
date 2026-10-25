using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Desktop.Updaters;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsTun;

// Install on an install from our site hands the package to the install's own updater, vhupdate.ps1 in its root, as
// Linux's vhupdate takes it: the updater checks the feed's version, then runs the setup silently, which stops this
// service, closes what runs from the install - the windows and trays it lists, which the new service opens again
// (WindowsUiRelaunch) - puts the new version in and starts it. It is the automatic update too: run by the setup's
// <instance>Updater task while updates are on. Here it is a child of this service, which it outlives: Windows ends
// no process with the one that started it. Its output goes to update.log beside it, whose last line says why.
public sealed class WindowsAppUpdaterProvider : DesktopAppUpdaterProvider
{
    private const string UpdaterFileName = "vhupdate.ps1";
    private readonly string _installRootPath;
    private readonly string _instanceName;

    private WindowsAppUpdaterProvider(string installRootPath, string instanceName, Uri updateInfoUrl,
        Version appVersion)
        : base(Path.Combine(installRootPath, "updates"), Path.Combine(installRootPath, MissedFileName), updateInfoUrl,
            appVersion)
    {
        _installRootPath = installRootPath;
        _instanceName = instanceName;
    }

    /// <summary>
    /// The updater of an install the setup made, whose version's folder holds the setup's publish.json; null for a
    /// copy run where it lies, a debugger's build.
    /// </summary>
    public static WindowsAppUpdaterProvider? TryCreate(AppOptionsContext context)
    {
        return TryCreate(new WindowsDesktopPaths(context.AppId), context.AppVersion);
    }

    public static WindowsAppUpdaterProvider? TryCreate(WindowsDesktopPaths paths, Version appVersion)
    {
        if (paths.InstallRootPath is not { } installRootPath ||
            !File.Exists(Path.Combine(installRootPath, UpdaterFileName)) ||
            ReadUpdateInfoUrl(Path.Combine(AppContext.BaseDirectory, "publish.json")) is not { } updateInfoUrl)
            return null;

        return new WindowsAppUpdaterProvider(installRootPath, paths.InstanceName, updateInfoUrl, appVersion);
    }

    private string TaskName => $"{_instanceName}Updater";
    private static string SchTasksPath => Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    // the task's choice, which the setup makes and keeps: its XML's Enabled, which only a disabled task says
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

    // The task on or off, as the setup leaves it; a run under way ends as it would. Turned on, it runs once at once,
    // as Linux's timer runs its updater when it starts: a check now rather than at the next boot or 12 hours on.
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

    // the task's run, whose triggers keep their times; one while it runs is ignored (IgnoreNew)
    protected override async Task RunUpdater(CancellationToken cancellationToken)
    {
        var result = await RunTool(SchTasksPath, ["/Run", "/TN", TaskName], cancellationToken).Vhc();
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"schtasks could not run the {TaskName} task. {result.Error.Trim()}");
    }

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
