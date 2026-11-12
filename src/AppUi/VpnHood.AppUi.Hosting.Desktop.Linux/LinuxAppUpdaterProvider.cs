using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Updaters;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.Linux;

/// <summary>
/// Provides the app's update on Linux, for an install from the website: it hands the package to the installer's own
/// updater, vhupdate, which is the automatic update too.
/// </summary>
/// <remarks>
/// The timer's runs of vhupdate check the feed; an install here hands vhupdate the downloaded package, whose own update
/// code and version it checks. Here it runs as a transient unit of its own, as a child of the service would go with the
/// service the install stops; its output goes to <c>update.log</c> in the storage folder.
/// </remarks>
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
    /// Creates the updater of the install that holds the app's storage folder, <c>/opt/&lt;name&gt;/storage</c> by default:
    /// one is there where the storage folder's parent holds a <c>publish.json</c> that names the feed vhupdate reads too.
    /// </summary>
    /// <param name="context">The app's options context, with its storage folder and version.</param>
    /// <returns>The updater, or <see langword="null"/> where there is no such file, as in a debugger's run.</returns>
    /// <exception cref="System.IO.InvalidDataException">The <c>UpdateInfoUrl</c> property in <c>publish.json</c> is null.</exception>
    public static LinuxAppUpdaterProvider? TryCreate(AppOptionsContext context)
    {
        return TryCreate(context.StoragePath, context.AppVersion);
    }

    private static LinuxAppUpdaterProvider? TryCreate(string storagePath, Version appVersion)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(storagePath)) is not { } installPath ||
            ReadUpdateInfoUrl(Path.Combine(installPath, "publish.json")) is not { } updateInfoUrl)
            return null;

        return new LinuxAppUpdaterProvider(installPath, storagePath, updateInfoUrl, appVersion);
    }

    private string TimerName => $"{_installName}Updater.timer";

    /// <summary>Gets whether the updater's timer is on, as the installer left it or the person set it.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is <see langword="true"/> where the timer is enabled.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="InvalidOperationException">systemctl could not be run.</exception>
    public override async Task<bool> IsAutomaticUpdateOn(CancellationToken cancellationToken)
    {
        var result = await RunTool("systemctl", ["is-enabled", "--quiet", TimerName], cancellationToken).Vhc();
        return result.ExitCode == 0;
    }

    /// <summary>Turns the updater's timer on or off at once, as the installer does.</summary>
    /// <param name="isOn"><see langword="true"/> to turn it on.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>Turned on, the timer schedules the updater's runs, as its unit sets them.</remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="InvalidOperationException">systemctl could not be run, or could not enable or disable the timer.</exception>
    public override async Task SetAutomaticUpdate(bool isOn, CancellationToken cancellationToken)
    {
        var verb = isOn ? "enable" : "disable";
        var result = await RunTool("systemctl", [verb, "--now", "--quiet", TimerName], cancellationToken).Vhc();
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"systemctl could not {verb} {TimerName}. {result.Error.Trim()}");
    }

    /// <summary>
    /// Asks systemd to start a run of the updater now, through the timer's own unit, as the timer does; a run under way
    /// takes the request.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once systemd has taken the request.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="InvalidOperationException">systemctl could not be run, or could not queue the start.</exception>
    protected override async Task RunUpdater(CancellationToken cancellationToken)
    {
        var unitName = $"{_installName}Updater.service";
        var result = await RunTool("systemctl", ["start", "--no-block", unitName], cancellationToken).Vhc();
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"systemctl could not start {unitName}. {result.Error.Trim()}");
    }

    /// <summary>Runs the installer's updater on a package, in a transient unit of its own, and waits for it.</summary>
    /// <param name="packagePath">The package.</param>
    /// <returns>
    /// A task whose result is <see langword="null"/> where the <c>systemd-run</c> process exits with a code above 128, taken
    /// as the install stopping this service; else a diagnostic: the update log's last line that is not blank,
    /// <c>systemd-run</c>'s error output, or a message with its exit code.
    /// </returns>
    /// <exception cref="InvalidOperationException">systemd-run could not be started.</exception>
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
