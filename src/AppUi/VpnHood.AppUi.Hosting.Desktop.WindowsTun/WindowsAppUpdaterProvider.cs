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
        : base(Path.Combine(installRootPath, "updates"), updateInfoUrl, appVersion)
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

    // the task's choice, which the setup makes and keeps: its XML's Enabled, which only a disabled task says
    protected override async Task<bool> IsAutomaticUpdateOn()
    {
        var startInfo = new ProcessStartInfo {
            FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "/Query", "/TN", $"{_instanceName}Updater", "/XML" })
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not run schtasks.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().Vhc();
        await Task.WhenAll(outputTask, errorTask).Vhc();
        if (process.ExitCode != 0)
            return false;

        var enabled = XDocument.Parse(outputTask.Result).Root?
            .Element(XName.Get("Settings", "http://schemas.microsoft.com/windows/2004/02/mit/task"))?
            .Element(XName.Get("Enabled", "http://schemas.microsoft.com/windows/2004/02/mit/task"))?.Value;
        return enabled != "false";
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
