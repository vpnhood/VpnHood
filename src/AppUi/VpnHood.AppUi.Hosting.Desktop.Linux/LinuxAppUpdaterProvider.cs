using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Desktop.Updaters;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.Linux;

// Install on an install from our site hands the package to the installer's own updater, vhupdate, which checks the
// feed's update code and version as its timer's runs do, and which is the automatic update too: run by
// <name>Updater.timer while updates are on, with -waitForIdle. Here vhupdate runs as a transient unit of its own: a
// child of the service would go with the service the install stops, and a start of the updater's own unit is the
// timer's to make. Its output goes to update.log in the storage folder, whose last line says why.
public sealed class LinuxAppUpdaterProvider : DesktopAppUpdaterProvider
{
    private readonly string _updaterPath;
    private readonly string _installName;
    private readonly string _logPath;

    private LinuxAppUpdaterProvider(string installPath, string storagePath, Uri updateInfoUrl, Version appVersion)
        : base(Path.Combine(storagePath, "updates"), Path.Combine(installPath, MissedFileName), updateInfoUrl,
            appVersion)
    {
        _updaterPath = Path.Combine(installPath, "vhupdate");
        _installName = Path.GetFileName(installPath);
        _logPath = Path.Combine(storagePath, "update.log");
    }

    /// <summary>
    /// The updater of the install that holds the storage folder, /opt/&lt;name&gt;/storage beside its
    /// publish.json, whose feed vhupdate reads too; null outside an install, as in a debugger's run.
    /// </summary>
    public static LinuxAppUpdaterProvider? TryCreate(AppOptionsContext context)
    {
        return TryCreate(context.StoragePath, context.AppVersion);
    }

    public static LinuxAppUpdaterProvider? TryCreate(string storagePath, Version appVersion)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(storagePath)) is not { } installPath ||
            ReadUpdateInfoUrl(Path.Combine(installPath, "publish.json")) is not { } updateInfoUrl)
            return null;

        return new LinuxAppUpdaterProvider(installPath, storagePath, updateInfoUrl, appVersion);
    }

    private string TimerName => $"{_installName}Updater.timer";

    // the timer's choice, which the installer makes and keeps
    public override async Task<bool> IsAutomaticUpdateOn(CancellationToken cancellationToken)
    {
        var result = await RunTool("systemctl", ["is-enabled", "--quiet", TimerName], cancellationToken).Vhc();
        return result.ExitCode == 0;
    }

    // --now, as the installer leaves it: the timer starts or stops at once, and a run under way ends as it would.
    // Started past boot's five minutes, the timer runs the updater at once: a check now, as on Windows.
    public override async Task SetAutomaticUpdate(bool isOn, CancellationToken cancellationToken)
    {
        var verb = isOn ? "enable" : "disable";
        var result = await RunTool("systemctl", [verb, "--now", "--quiet", TimerName], cancellationToken).Vhc();
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"systemctl could not {verb} {TimerName}. {result.Error.Trim()}");
    }

    // the timer's own unit, which counts its next run from this one; a start while one runs joins it
    protected override async Task RunUpdater(CancellationToken cancellationToken)
    {
        var unitName = $"{_installName}Updater.service";
        var result = await RunTool("systemctl", ["start", "--no-block", unitName], cancellationToken).Vhc();
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"systemctl could not start {unitName}. {result.Error.Trim()}");
    }

    protected override async Task<string?> RunInstaller(string packagePath)
    {
        // systemd's file: output writes from the start without truncating
        File.Delete(_logPath);

        var unitName = $"{_installName}UpdateNow";
        var startInfo = new ProcessStartInfo {
            FileName = "systemd-run",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] {
                     $"--unit={unitName}", "--collect", "--wait", "--quiet",
                     $"--property=StandardOutput=file:{_logPath}", _updaterPath, "-packageFile", packagePath
                 })
            startInfo.ArgumentList.Add(argument);

        VhLogger.Instance.LogInformation("Installing the update. Unit: {Unit}", unitName);
        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Could not run systemd-run.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().Vhc();
        await Task.WhenAll(outputTask, errorTask).Vhc();
        VhLogger.Instance.LogInformation("The update's unit ended. ExitCode: {ExitCode}", process.ExitCode);

        // killed by a signal with this service, which the install stops: the update goes on in its own unit
        if (process.ExitCode > 128)
            return null;

        var message = ReadLastLogLine() ?? errorTask.Result.Trim();
        return string.IsNullOrEmpty(message) ? $"The updater ended with code {process.ExitCode}." : message;
    }

    private string? ReadLastLogLine()
    {
        return File.Exists(_logPath)
            ? File.ReadLines(_logPath).LastOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim()
            : null;
    }
}
