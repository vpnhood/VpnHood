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
// downloads the feed's package into the storage's updates folder; Install hands it to the platform's installer,
// whose install stops this service, replaces it and starts the new version: an Install that ends with this service
// still here installed nothing.
public abstract class DesktopAppUpdaterProvider(string storagePath, Uri updateInfoUrl) : IAppUpdaterProvider
{
    private static readonly HttpClient HttpClient = new();
    private readonly string _updatesPath = Path.Combine(storagePath, "updates");
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
                // an update installed or overtaken: its package goes
                if (!isBusy)
                    DeletePackages();
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
            DeletePackages();

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

    // every file of the updates folder: a download starts whole, and an installed or overtaken update is done
    private void DeletePackages()
    {
        if (!Directory.Exists(_updatesPath))
            return;

        foreach (var filePath in Directory.EnumerateFiles(_updatesPath))
            VhInvoker.TryInvoke("delete an old update", LogLevel.Warning, () => File.Delete(filePath));
    }
}
