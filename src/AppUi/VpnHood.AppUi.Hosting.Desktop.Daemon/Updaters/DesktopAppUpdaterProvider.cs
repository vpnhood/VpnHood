using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Updaters;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Updaters;

/// <summary>
/// Provides the app's update on a desktop installed from the website, in the service: the notice's Update and Install,
/// and the automatic update's part.
/// </summary>
/// <remarks>
/// The feed is the one the install's <c>publish.json</c> names. Update downloads the feed's package into the install's
/// updates folder; Install hands it to the platform's installer, whose install stops this service, replaces it and starts
/// the new version, so an Install that ends with this service still here installed nothing. The automatic update is
/// the platform's updater, a script apart from the service, which shares the updates folder; a run of it that could not
/// reach the server leaves a file, and the service may run it again after a later connect, while automatic updates are
/// on.
/// </remarks>
/// <param name="updatesPath">The install's updates folder.</param>
/// <param name="missedPath">The file the updater leaves while its last run could not reach the server.</param>
/// <param name="updateInfoUrl">The feed.</param>
/// <param name="appVersion">This app's version.</param>
public abstract class DesktopAppUpdaterProvider(string updatesPath, string missedPath, Uri updateInfoUrl,
    Version appVersion) : IAppUpdaterProvider
{
    /// <summary>The name of the file the updater leaves while its last run could not reach the server.</summary>
    protected const string MissedFileName = "update.missed";

    private static readonly HttpClient HttpClient = new();
    private readonly Lock _stateLock = new();

    // this service's update, for one release: a restart forgets it, as an install ends it
    private Version? _version;
    private Task? _downloadTask;
    private long _downloadedBytes;
    private long? _totalBytes;
    private bool _isInstalling;
    private string? _errorMessage;

    /// <summary>Gets the feed, as the install's <c>publish.json</c> names it.</summary>
    public Uri UpdateInfoUrl { get; } = updateInfoUrl;

    /// <inheritdoc/>
    /// <remarks>It is <see langword="false"/>: the app's notice shows the update.</remarks>
    public bool HasOwnScreen => false;

    /// <inheritdoc/>
    public bool IsInstalling {
        get {
            lock (_stateLock)
                return _isInstalling;
        }
    }

    /// <summary>Runs the platform's installer on a package.</summary>
    /// <param name="packagePath">The package.</param>
    /// <returns>
    /// A task whose result is <see langword="null"/> once its install is stopping this service, which the install goes on
    /// without; else why it installed nothing.
    /// </returns>
    /// <exception cref="InvalidOperationException">The installer could not be started.</exception>
    protected abstract Task<string?> RunInstaller(string packagePath);

    /// <inheritdoc/>
    /// <remarks>It is <see langword="true"/>: the platform's updater is the automatic update.</remarks>
    public bool IsAutomaticUpdateSupported => true;

    /// <inheritdoc/>
    /// <remarks>The platform's updater runs by itself where its timer or task is on, as its installer left it.</remarks>
    /// <exception cref="InvalidOperationException">The platform's tool could not be started.</exception>
    public abstract Task<bool> IsAutomaticUpdateOn(CancellationToken cancellationToken);

    /// <inheritdoc/>
    /// <remarks>It turns the timer or the task on or off, which the installers keep at an update.</remarks>
    public abstract Task SetAutomaticUpdate(bool isOn, CancellationToken cancellationToken);

    /// <summary>Starts a run of the platform's updater now, as its timer or task does, which goes on by itself.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the run is asked for.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="InvalidOperationException">The platform's tool could not be started, or could not start the run.</exception>
    protected abstract Task RunUpdater(CancellationToken cancellationToken);

    /// <summary>Runs a platform tool to its end.</summary>
    /// <param name="fileName">The tool.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the tool's exit code and what it wrote to its output and error streams.</returns>
    /// <exception cref="InvalidOperationException">The tool could not be run.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
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
    /// Installs, while automatic updates are on, a release downloaded already, by the updater while the VPN was in use or by
    /// the notice's Update, and newer than this app: the automatic update's part in the service, with the VPN idle again.
    /// </summary>
    /// <remarks>
    /// An install stops this service; else it logs why there was none. A cancel is seen until the install is committed,
    /// just before the installer starts; the installer, once started, runs to its end. It does not run beside the notice's
    /// own Install or Update.
    /// </remarks>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the installer has ended, or the install was put off.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    internal async Task InstallDownloadedUpdate(CancellationToken cancellationToken)
    {
        try {
            if (FindDownload() is not { } packagePath || !await IsAutomaticUpdateOn(cancellationToken).Vhc())
                return;

            lock (_stateLock) {
                if (_isInstalling || _downloadTask is { IsCompleted: false })
                    return;

                // the cancel's last chance: from here the app is Updating, which refuses a connect
                cancellationToken.ThrowIfCancellationRequested();
                _isInstalling = true;
            }

            VhLogger.Instance.LogInformation("Installing the downloaded update. Package: {Package}",
                Path.GetFileName(packagePath));
            var isStopping = false;
            try {
                if (await RunInstaller(packagePath).Vhc() is { } message)
                    VhLogger.Instance.LogWarning("The downloaded update was not installed. Message: {Message}", message);
                else
                    isStopping = true;
            }
            finally {
                // an install stopping this service is installing to the end
                if (!isStopping)
                    lock (_stateLock)
                        _isInstalling = false;
            }
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not install the downloaded update.");
        }
    }

    /// <summary>
    /// Runs the updater again where its last run could not reach the feed or its package, while automatic updates are on:
    /// the automatic update's part in the service at a connect.
    /// </summary>
    /// <remarks>
    /// A filtered network may let it through only in the tunnel. What it downloads waits for the disconnect, as any download
    /// made while the VPN is in use.
    /// </remarks>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    internal async Task RetryMissedUpdate(CancellationToken cancellationToken)
    {
        try {
            if (!File.Exists(missedPath) || !await IsAutomaticUpdateOn(cancellationToken).Vhc())
                return;

            VhLogger.Instance.LogInformation("Running the updater again: its last run could not reach the server.");
            await RunUpdater(cancellationToken).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not run the updater again.");
        }
    }

    // the newest whole download of a release newer than this app
    private string? FindDownload()
    {
        string? newestPath = null;
        var newestVersion = appVersion;
        foreach (var (path, version) in EnumerateDownloads(isPartIncluded: false)) {
            if (version > newestVersion) {
                newestPath = path;
                newestVersion = version;
            }
        }

        return newestPath;
    }

    // The downloads in the updates folder, "<version>-<file>" as GetPackagePath names them: the whole ones, and with
    // the parts the app's cut or under way too (.part). The updater's own under way are neither: their names are no
    // version.
    private IEnumerable<(string Path, Version Version)> EnumerateDownloads(bool isPartIncluded)
    {
        if (!Directory.Exists(updatesPath))
            yield break;

        foreach (var path in Directory.EnumerateFiles(updatesPath)) {
            var fileName = Path.GetFileName(path);
            if ((isPartIncluded || !fileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) &&
                Version.TryParse(fileName.Split('-')[0], out var version))
                yield return (path, version);
        }
    }

    // The downloads up to a version, whole or cut: those of this version and older, installed; or those older than
    // the release a download is for, overtaken.
    private void RemoveDownloads(Version version, bool isVersionIncluded)
    {
        foreach (var download in EnumerateDownloads(isPartIncluded: true).ToArray()) {
            if (download.Version < version || (isVersionIncluded && download.Version == version))
                VhInvoker.TryInvoke("delete an old update", LogLevel.Warning, () => File.Delete(download.Path));
        }
    }

    /// <summary>Reads the feed a <c>publish.json</c> names.</summary>
    /// <param name="publishInfoPath">The <c>publish.json</c>.</param>
    /// <returns>The feed, or <see langword="null"/> where there is no such file, as outside an install.</returns>
    /// <exception cref="InvalidDataException">The <c>UpdateInfoUrl</c> property is null.</exception>
    protected static Uri? ReadUpdateInfoUrl(string publishInfoPath)
    {
        if (!File.Exists(publishInfoPath))
            return null;

        using var publishInfo = JsonDocument.Parse(File.ReadAllText(publishInfoPath));
        var updateInfoUrl = publishInfo.RootElement.GetProperty("UpdateInfoUrl").GetString() ??
                            throw new InvalidDataException($"{publishInfoPath} names no UpdateInfoUrl.");
        return new Uri(updateInfoUrl);
    }

    /// <inheritdoc/>
    /// <remarks>It reads only this service's own update and the updates folder: no network. The token is not read.</remarks>
    public Task<AppUpdateState> Check(AppRelease? feedRelease, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(GetState(feedRelease));
    }

    /// <inheritdoc/>
    public AppUpdateState GetState(AppRelease? feedRelease)
    {
        lock (_stateLock) {
            var isBusy = _isInstalling || _downloadTask is { IsCompleted: false };
            if (feedRelease is null) {
                // an update installed: its package goes, and none newer, which the updater may have downloaded since
                // the feed was read
                if (!isBusy)
                    RemoveDownloads(appVersion, isVersionIncluded: true);
                return AppUpdateState.None;
            }

            // the notice's Install, or a download the updater left and the service handed back
            if (_isInstalling)
                return new AppUpdateState { Stage = AppUpdateStage.Installing };

            if (_version == feedRelease.Version) {
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

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// There is no newer release, or its package is neither on https nor on this machine.
    /// </exception>
    /// <remarks>The token is not read.</remarks>
    public Task StartUpdate(AppRelease? feedRelease, IUiContext? uiContext, CancellationToken cancellationToken)
    {
        if (feedRelease is null)
            throw new InvalidOperationException("There is no newer release to update to.");

        // https alone, or a test feed on this machine. HTTPS is the check; add no release key: whoever can change our
        // GitHub releases can change far more than a signature on the package would stop
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

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">There is no newer release, or it has not been downloaded.</exception>
    /// <remarks>The token is not read.</remarks>
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

    /// <inheritdoc/>
    /// <remarks>It is <see langword="false"/>: nothing here waits for the person. The token is not read.</remarks>
    public Task<bool> ResumeUpdate(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    // per release, named as the feed names it
    private string GetPackagePath(AppRelease release)
    {
        return Path.Combine(updatesPath, $"{release.Version}-{Path.GetFileName(release.PackageUrl.AbsolutePath)}");
    }

    // A cut download keeps its .part, which the next Update continues by a range request; a server that ignores the
    // range sends the whole file again, which starts it over. The resume need not check that the server's file is
    // still the same: the package's address names its version, so only an upload replaced under the same version
    // could splice two files, and a spliced package fails its own check - NSIS's CRC, gzip's - before its install
    // stops anything, and goes.
    private async Task Download(AppRelease release)
    {
        var packagePath = GetPackagePath(release);
        var partPath = packagePath + ".part";
        try {
            VhLogger.Instance.LogInformation("Downloading the update. Version: {Version}", release.Version);
            Directory.CreateDirectory(updatesPath);
            RemoveDownloads(release.Version, isVersionIncluded: false);

            var partBytes = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
            using var response = await RequestPackage(release.PackageUrl, partBytes).Vhc();
            response.EnsureSuccessStatusCode();

            var isContinued = response.StatusCode == HttpStatusCode.PartialContent;
            if (isContinued && response.Content.Headers.ContentRange?.From != partBytes) {
                File.Delete(partPath);
                throw new InvalidDataException("The server sent another part of the update than the one asked for.");
            }

            if (isContinued)
                VhLogger.Instance.LogInformation("Continuing the cut download. Version: {Version}, From: {Bytes} bytes",
                    release.Version, partBytes);

            var totalBytes = isContinued
                ? response.Content.Headers.ContentRange?.Length
                : response.Content.Headers.ContentLength;
            Interlocked.Exchange(ref _downloadedBytes, isContinued ? partBytes : 0);
            lock (_stateLock)
                _totalBytes = totalBytes;

            await WritePart(response, partPath, isContinued).Vhc();

            // a cut connection may end the stream early: only the whole file becomes the package
            var downloadedBytes = Interlocked.Read(ref _downloadedBytes);
            if (totalBytes != null && downloadedBytes != totalBytes)
                throw new IOException($"The download ended at {downloadedBytes} of {totalBytes} bytes.");

            File.Move(partPath, packagePath, overwrite: true);
            VhLogger.Instance.LogInformation("The update has been downloaded. Version: {Version}", release.Version);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not download the update. Version: {Version}", release.Version);
            lock (_stateLock)
                _errorMessage = ex.Message;
        }
    }

    // The package from the byte given on; a part the server's file ends at or before, as an upload replaced by a
    // shorter one, is asked for again whole.
    private static async Task<HttpResponseMessage> RequestPackage(Uri packageUrl, long fromBytes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, packageUrl);
        if (fromBytes > 0)
            request.Headers.Range = new RangeHeaderValue(fromBytes, null);

        var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).Vhc();
        if (fromBytes == 0 || response.StatusCode != HttpStatusCode.RequestedRangeNotSatisfiable)
            return response;

        response.Dispose();
        return await RequestPackage(packageUrl, 0).Vhc();
    }

    // the response's body into the part: after its bytes when it continues it, else in their place
    private async Task WritePart(HttpResponseMessage response, string partPath, bool isContinued)
    {
        await using var source = await response.Content.ReadAsStreamAsync().Vhc();
        await using var target = new FileStream(partPath, isContinued ? FileMode.Append : FileMode.Create,
            FileAccess.Write);
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer).Vhc()) > 0) {
            await target.WriteAsync(buffer.AsMemory(0, read)).Vhc();
            Interlocked.Add(ref _downloadedBytes, read);
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
