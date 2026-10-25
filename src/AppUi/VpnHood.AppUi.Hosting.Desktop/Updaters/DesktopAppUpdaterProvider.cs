using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Updaters;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppUi.Hosting.Desktop.Updaters;

// The notice's Update and Install on a desktop installed from our site, as on Android's website APK, in the service.
// The feed is the one the install's publish.json names, the release's info file its installer put there. Update
// downloads the feed's package into the install's updates folder; Install hands it to the platform's installer,
// whose install stops this service, replaces it and starts the new version: an Install that ends with this service
// still here installed nothing. The automatic update is the platform's updater, a script apart from the service so
// that a broken release still gets the next one: it downloads into the same folder under the same name, and leaves
// a download made while the VPN was in use for the service to hand back after the next disconnect
// (InstallDownloadedUpdate). So the folder is shared, and each side removes only what it is done with: this one the
// downloads of this version and older, and of one overtaken by the release it downloads. A run of the updater that
// could not reach the server says so in a file of its own, and the service runs the updater again at the next connect
// (RetryMissedUpdate).
public abstract class DesktopAppUpdaterProvider(string updatesPath, string missedPath, Uri updateInfoUrl,
    Version appVersion) : IAppUpdaterProvider
{
    // what the updater leaves while its last run could not reach the server (updater.sh, install-client-updater.ps1)
    protected const string MissedFileName = "update.missed";

    private static readonly HttpClient HttpClient = new();
    private readonly string _updatesPath = updatesPath;
    private readonly string _missedPath = missedPath;
    private readonly Version _appVersion = appVersion;
    private readonly Lock _stateLock = new();

    // this service's update, for one release: a restart forgets it, as an install ends it
    private Version? _version;
    private Task? _downloadTask;
    private long _downloadedBytes;
    private long? _totalBytes;
    private bool _isInstalling;
    private string? _errorMessage;

    public Uri UpdateInfoUrl { get; } = updateInfoUrl;

    public bool HasOwnScreen => false;

    // Runs the platform's installer on the package: null once its install is stopping this service, which the
    // install goes on without, else why it installed nothing.
    protected abstract Task<string?> RunInstaller(string packagePath);

    public bool IsAutomaticUpdateSupported => true;

    // whether the platform's updater runs by itself: its timer or task on, as its installer left it
    public abstract Task<bool> IsAutomaticUpdateOn(CancellationToken cancellationToken);

    // the timer or the task on or off, which the installers keep at an update
    public abstract Task SetAutomaticUpdate(bool isOn, CancellationToken cancellationToken);

    // a run of the platform's updater now, as its timer or task starts one, going on by itself
    protected abstract Task RunUpdater(CancellationToken cancellationToken);

    // a platform tool's run to its end: its exit code and what it wrote
    protected static async Task<(int ExitCode, string Output, string Error)> RunTool(string fileName,
        IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not run {fileName}.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).Vhc();
        return (process.ExitCode, await outputTask.Vhc(), await errorTask.Vhc());
    }

    /// <summary>
    /// The automatic update's part in the service, with the VPN idle again: a release downloaded already - by the
    /// updater while the VPN was in use, or by the notice's Update - and newer than this app, installed now, while
    /// automatic updates are on. An install stops this service; else this logs why there was none.
    /// </summary>
    public async Task InstallDownloadedUpdate()
    {
        try {
            if (FindDownload() is not { } packagePath || !await IsAutomaticUpdateOn(CancellationToken.None).Vhc())
                return;

            VhLogger.Instance.LogInformation("Installing the downloaded update. Package: {Package}",
                Path.GetFileName(packagePath));
            if (await RunInstaller(packagePath).Vhc() is { } message)
                VhLogger.Instance.LogWarning("The downloaded update was not installed. Message: {Message}", message);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not install the downloaded update.");
        }
    }

    /// <summary>
    /// The automatic update's part in the service at a connect: a run of the updater that could not reach the feed or
    /// its package - a filtered network may let it through only in the tunnel - is made again now, while automatic
    /// updates are on. What it downloads waits for the disconnect, as any download made while the VPN is in use.
    /// </summary>
    public async Task RetryMissedUpdate()
    {
        try {
            if (!File.Exists(_missedPath) || !await IsAutomaticUpdateOn(CancellationToken.None).Vhc())
                return;

            VhLogger.Instance.LogInformation("Running the updater again: its last run could not reach the server.");
            await RunUpdater(CancellationToken.None).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not run the updater again.");
        }
    }

    // the newest whole download of a release newer than this app
    private string? FindDownload()
    {
        string? newestPath = null;
        var newestVersion = _appVersion;
        foreach (var (path, version) in EnumerateDownloads()) {
            if (version > newestVersion) {
                newestPath = path;
                newestVersion = version;
            }
        }

        return newestPath;
    }

    // The whole downloads in the updates folder, "<version>-<file>" as GetPackagePath names them. A download under way
    // is neither: a .part of the app's, or a file of the updater's whose name is no version.
    private IEnumerable<(string Path, Version Version)> EnumerateDownloads()
    {
        if (!Directory.Exists(_updatesPath))
            yield break;

        foreach (var path in Directory.EnumerateFiles(_updatesPath)) {
            var fileName = Path.GetFileName(path);
            if (!fileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                Version.TryParse(fileName.Split('-')[0], out var version))
                yield return (path, version);
        }
    }

    // The whole downloads up to a version: those of this version and older, installed; or those older than the
    // release a download is for, overtaken.
    private void RemoveDownloads(Version version, bool isVersionIncluded)
    {
        foreach (var download in EnumerateDownloads().ToArray()) {
            if (download.Version < version || (isVersionIncluded && download.Version == version))
                VhInvoker.TryInvoke("delete an old update", LogLevel.Warning, () => File.Delete(download.Path));
        }
    }

    // the feed a publish.json names; null where there is none, as outside an install
    protected static Uri? ReadUpdateInfoUrl(string publishInfoPath)
    {
        if (!File.Exists(publishInfoPath))
            return null;

        using var publishInfo = JsonDocument.Parse(File.ReadAllText(publishInfoPath));
        var updateInfoUrl = publishInfo.RootElement.GetProperty("UpdateInfoUrl").GetString() ??
                            throw new InvalidDataException($"{publishInfoPath} names no UpdateInfoUrl.");
        return new Uri(updateInfoUrl);
    }

    // this service's own update and the updates folder are all there is to ask: no network
    public Task<AppUpdateState> Check(AppRelease? feedRelease, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(GetState(feedRelease));
    }

    public AppUpdateState GetState(AppRelease? feedRelease)
    {
        lock (_stateLock) {
            var isBusy = _isInstalling || _downloadTask is { IsCompleted: false };
            if (feedRelease is null) {
                // an update installed: its package goes, and none newer, which the updater may have downloaded since
                // the feed was read
                if (!isBusy)
                    RemoveDownloads(_appVersion, isVersionIncluded: true);
                return AppUpdateState.None;
            }

            if (_version == feedRelease.Version) {
                if (_isInstalling)
                    return new AppUpdateState { Stage = AppUpdateStage.Installing };

                if (_downloadTask is { IsCompleted: false })
                    return new AppUpdateState {
                        Stage = AppUpdateStage.Downloading,
                        DownloadedBytes = Interlocked.Read(ref _downloadedBytes),
                        TotalBytes = _totalBytes
                    };

                if (_errorMessage != null)
                    return new AppUpdateState { Stage = AppUpdateStage.Failed, ErrorMessage = _errorMessage };
            }

            return File.Exists(GetPackagePath(feedRelease))
                ? new AppUpdateState { Stage = AppUpdateStage.Downloaded }
                : new AppUpdateState { Stage = AppUpdateStage.Available };
        }
    }

    public Task StartUpdate(AppRelease? feedRelease, IUiContext? uiContext, CancellationToken cancellationToken)
    {
        if (feedRelease is null)
            throw new InvalidOperationException("There is no newer release to update to.");

        // https alone, or a test feed on this machine
        if (feedRelease.PackageUrl.Scheme != Uri.UriSchemeHttps && !feedRelease.PackageUrl.IsLoopback)
            throw new InvalidOperationException($"The package is not on https: {feedRelease.PackageUrl}");

        lock (_stateLock) {
            if (_isInstalling || _downloadTask is { IsCompleted: false })
                return Task.CompletedTask;

            _version = feedRelease.Version;
            _errorMessage = null;
            _downloadedBytes = 0;
            _totalBytes = null;
            // never cut by the caller, whose request ends at once
            _downloadTask = Task.Run(() => Download(feedRelease), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public Task CompleteUpdate(AppRelease? feedRelease, IUiContext? uiContext, CancellationToken cancellationToken)
    {
        if (feedRelease is null)
            throw new InvalidOperationException("There is no newer release to install.");

        var packagePath = GetPackagePath(feedRelease);
        if (!File.Exists(packagePath))
            throw new InvalidOperationException("The update has not been downloaded.");

        lock (_stateLock) {
            if (_isInstalling || _downloadTask is { IsCompleted: false })
                return Task.CompletedTask;

            _version = feedRelease.Version;
            _errorMessage = null;
            _isInstalling = true;
            // never cut by the caller: the install it starts must finish
            _ = Task.Run(() => Install(packagePath), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ResumeUpdate(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    // per release, named as the feed names it
    private string GetPackagePath(AppRelease release)
    {
        return Path.Combine(_updatesPath, $"{release.Version}-{Path.GetFileName(release.PackageUrl.AbsolutePath)}");
    }

    private async Task Download(AppRelease release)
    {
        var packagePath = GetPackagePath(release);
        var partPath = packagePath + ".part";
        try {
            VhLogger.Instance.LogInformation("Downloading the update. Version: {Version}", release.Version);
            Directory.CreateDirectory(_updatesPath);
            RemoveDownloads(release.Version, isVersionIncluded: false);

            using var response = await HttpClient
                .GetAsync(release.PackageUrl, HttpCompletionOption.ResponseHeadersRead).Vhc();
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength;
            lock (_stateLock)
                _totalBytes = totalBytes;

            await using (var source = await response.Content.ReadAsStreamAsync().Vhc())
            await using (var target = File.Create(partPath)) {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer).Vhc()) > 0) {
                    await target.WriteAsync(buffer.AsMemory(0, read)).Vhc();
                    Interlocked.Add(ref _downloadedBytes, read);
                }
            }

            // a cut connection may end the stream early: only the whole file becomes the package
            var downloadedBytes = Interlocked.Read(ref _downloadedBytes);
            if (totalBytes != null && downloadedBytes != totalBytes)
                throw new IOException($"The download ended at {downloadedBytes} of {totalBytes} bytes.");

            File.Move(partPath, packagePath, overwrite: true);
            VhLogger.Instance.LogInformation("The update has been downloaded. Version: {Version}", release.Version);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not download the update. Version: {Version}", release.Version);
            File.Delete(partPath);
            lock (_stateLock)
                _errorMessage = ex.Message;
        }
    }

    private async Task Install(string packagePath)
    {
        string? message;
        try {
            message = await RunInstaller(packagePath).Vhc();
            if (message is null)
                return;

            VhLogger.Instance.LogWarning("The update was not installed. Message: {Message}", message);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not install the update.");
            message = ex.Message;
        }

        lock (_stateLock) {
            _isInstalling = false;
            _errorMessage = message;
        }
    }
}
