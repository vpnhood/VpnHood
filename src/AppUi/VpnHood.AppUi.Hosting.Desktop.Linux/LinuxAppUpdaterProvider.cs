using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Desktop.Updaters;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.Linux;

// Install on an install from our site hands the package to the installer's own updater, vhupdate, which checks the
// feed's update code and version as its scheduled runs do. vhupdate runs as a transient unit of its own: a child of
// the service would go with the service the install stops, and the scheduled updater unit (Restart=always) would go
// on every 12 hours for a person who turned it off. Its output goes to update.log in the storage folder, whose last
// line says why.
public sealed class LinuxAppUpdaterProvider : DesktopAppUpdaterProvider
{
    private readonly string _updaterPath;
    private readonly string _unitName;
    private readonly string _logPath;

    private LinuxAppUpdaterProvider(string installPath, string storagePath, Uri updateInfoUrl)
        : base(storagePath, updateInfoUrl)
    {
        _updaterPath = Path.Combine(installPath, "vhupdate");
        _unitName = $"{Path.GetFileName(installPath)}UpdateNow";
        _logPath = Path.Combine(storagePath, "update.log");
    }

    /// <summary>
    /// The updater of the install that holds the storage folder, /opt/&lt;name&gt;/storage beside its
    /// publish.json, whose feed vhupdate reads too; null outside an install, as in a debugger's run.
    /// </summary>
    public static LinuxAppUpdaterProvider? TryCreate(AppOptionsContext context)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(context.StoragePath)) is not { } installPath ||
            ReadUpdateInfoUrl(Path.Combine(installPath, "publish.json")) is not { } updateInfoUrl)
            return null;

        return new LinuxAppUpdaterProvider(installPath, context.StoragePath, updateInfoUrl);
    }

    protected override async Task<string?> RunInstaller(string packagePath)
    {
        // systemd's file: output writes from the start without truncating
        File.Delete(_logPath);

        var startInfo = new ProcessStartInfo {
            FileName = "systemd-run",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] {
                     $"--unit={_unitName}", "--collect", "--wait", "--quiet",
                     $"--property=StandardOutput=file:{_logPath}", _updaterPath, "-packageFile", packagePath
                 })
            startInfo.ArgumentList.Add(argument);

        VhLogger.Instance.LogInformation("Installing the update. Unit: {Unit}", _unitName);
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
