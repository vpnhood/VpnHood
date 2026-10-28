using System.Runtime.InteropServices;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Provider;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Updaters;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.Devices.Android;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Net.Toolkit.Logging;
using AndroidUri = Android.Net.Uri;
using Uri = System.Uri;

namespace VpnHood.AppLib.Stores.AndroidWeb;

/// <summary>
/// The update of an APK handed out outside the stores. Android's DownloadManager downloads the package
/// in its own process, so a minimized or killed app and a disconnect do not stop it, and a
/// PackageInstaller session installs it once the person confirms. Nothing starts unpressed: the notice
/// offers it, as the person may be on mobile data. The package lives in the app's external cache folder,
/// which no backup takes, named by its version, so what to remove needs no record of its own:
/// DownloadManager lists only this app's downloads.
/// </summary>
public class AndroidWebAppUpdaterProvider : IAppUpdaterProvider
{
    private const string FolderName = "updates";
    private const string FilePrefix = "update-";
    private const string FileExtension = ".apk";
    private const string ApkMimeType = "application/vnd.android.package-archive";
    private const string InstallStatusAction = "VpnHood.AppLib.Stores.AndroidWeb.INSTALL_STATUS";

    private readonly Lock _lock = new();
    private InstallStatusReceiver? _installStatusReceiver;
    private Version? _installingVersion;
    private Version? _failedVersion;
    private string? _failedMessage;

    private AndroidWebAppUpdaterProvider()
    {
    }

    /// <summary>
    /// The updater, where this install may take the package. Null where it may not, and the notice keeps
    /// the feed's link: a store installed the app and keeps it updated. It checks no key: Android itself
    /// refuses a package signed with another key than the installed app's.
    /// </summary>
    public static AndroidWebAppUpdaterProvider? TryCreate()
    {
        return CalcIsSupported() ? new AndroidWebAppUpdaterProvider() : null;
    }

    /// <summary>
    /// The feed this install reads, as AppConfigs.GetUpdateInfoUrl names it: in an arm64 process the arm64
    /// APK's, a third the size, else the universal APK's. The process decides, not the build: the universal
    /// APK on an arm64 phone runs as arm64 too, so it moves to the arm64 APK at its next update. Every
    /// release publishes both feeds (pub/lib/Publish-GithubRelease.ps1); never name another.
    /// </summary>
    public static string FeedDistribution => RuntimeInformation.ProcessArchitecture == Architecture.Arm64
        ? "android-web-arm64"
        : "android-web";

    private static Context Context => Application.Context;

    private static DownloadManager DownloadManager =>
        Context.GetSystemService(Context.DownloadService) as DownloadManager ??
        throw new InvalidOperationException("This device has no DownloadManager.");

    // DownloadManager writes only to external storage, where Android's backups and transfers to a new
    // phone take the app's files folder but never its cache. Android may empty the cache when storage
    // runs low.
    private static string FolderPath => Path.Combine(
        Context.ExternalCacheDir?.AbsolutePath ??
        throw new InvalidOperationException("The app's external cache folder is not available."),
        FolderName);

    public bool HasOwnScreen => false;

    // the package installer's, the system's, which stops the app itself
    public bool IsInstalling => false;

    // DownloadManager's list is all there is to ask: no network
    public Task<AppUpdateState> Check(AppRelease? feedRelease, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(GetState(feedRelease));
    }

    public AppUpdateState GetState(AppRelease? feedRelease)
    {
        var version = feedRelease?.Version;
        lock (_lock) {
            PackageDownload? current = null;
            foreach (var download in QueryDownloads()) {
                // another version's package, finished or not: only the latest version's is kept
                if (version is null || download.Version != version) {
                    Remove(download);
                    continue;
                }

                // a failed download goes at once; its reason stays for the notice, and Update starts anew
                if (download.Status == DownloadStatus.Failed) {
                    SetFailed(version, $"The download failed. {DescribeReason(download.Reason)}");
                    Remove(download);
                    continue;
                }

                // a package gone from the cache folder: Update downloads it anew
                if (download.Status == DownloadStatus.Successful && !File.Exists(download.FilePath)) {
                    SetFailed(version, "The downloaded file was removed.");
                    Remove(download);
                    continue;
                }

                current = download;
            }

            // the current download's file, or the one it is about to write
            DeleteOtherFiles(current is null ? null : current.FilePath ?? Path.Combine(FolderPath, FileNameOf(current.Version)));

            if (version is null)
                return AppUpdateState.None;

            if (current != null)
                return new AppUpdateState {
                    Stage = current.Status != DownloadStatus.Successful ? AppUpdateStage.Downloading
                        : version == _installingVersion ? AppUpdateStage.Installing
                        : AppUpdateStage.Downloaded,
                    DownloadedBytes = current.DownloadedBytes,
                    TotalBytes = current.TotalBytes > 0 ? current.TotalBytes : null
                };

            return version == _failedVersion
                ? new AppUpdateState { Stage = AppUpdateStage.Failed, ErrorMessage = _failedMessage }
                : new AppUpdateState { Stage = AppUpdateStage.Available };
        }
    }

    public Task StartUpdate(AppRelease? feedRelease, IUiContext? uiContext, CancellationToken cancellationToken)
    {
        var release = feedRelease ?? throw new InvalidOperationException("There is no newer version to download.");
        if (!IsAllowedPackageUrl(release.PackageUrl))
            throw new NotSupportedException($"The package must come over https. PackageUrl: {release.PackageUrl}");

        lock (_lock) {
            // removes every other package, and this version's if it failed
            if (GetState(release).Stage is AppUpdateStage.Downloading or AppUpdateStage.Downloaded or
                AppUpdateStage.Installing)
                return Task.CompletedTask;

            _failedVersion = null;
            _failedMessage = null;

            using var request = new DownloadManager.Request(AndroidUri.Parse(release.PackageUrl.AbsoluteUri));
            Directory.CreateDirectory(FolderPath);
            using var file = new Java.IO.File(FolderPath, FileNameOf(release.Version));
            request.SetDestinationUri(AndroidUri.FromFile(file));

            // Android's notification shows the progress while it runs, but not the end: a tap on it
            // would open the package outside the app, so the app tells the person itself
            request.SetNotificationVisibility(DownloadVisibility.Visible);
            request.SetTitle(AndroidUtils.GetAppName(Context));
            request.SetDescription(release.Version.ToString());
            request.SetMimeType(ApkMimeType);
            var id = DownloadManager.Enqueue(request);
            VhLogger.Instance.LogInformation("DownloadManager is downloading the update. Version: {Version}, Id: {Id}",
                release.Version, id);
        }

        return Task.CompletedTask;
    }

    public async Task CompleteUpdate(AppRelease? feedRelease, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        var version = feedRelease?.Version ??
                      throw new InvalidOperationException("There is no newer version to install.");
        var activity = (uiContext as AndroidUiContext ?? throw new UiContextNotAvailableException()).Activity;
        var packageManager = activity.PackageManager ??
                             throw new InvalidOperationException("The activity has no PackageManager.");
        var packageName = activity.PackageName ??
                          throw new InvalidOperationException("The activity has no package name.");

        // Android 8 and later ask the person once to let this app install apps; their next press installs
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && !packageManager.CanRequestPackageInstalls()) {
            VhLogger.Instance.LogInformation("Asking the person to let the app install apps.");
            using var settingsIntent = new Intent(Settings.ActionManageUnknownAppSources,
                AndroidUri.Parse($"package:{packageName}"));
            activity.StartActivity(settingsIntent);
            return;
        }

        string filePath;
        lock (_lock) {
            // a second press while the first one hands the package over
            var stage = GetState(feedRelease).Stage;
            if (stage == AppUpdateStage.Installing)
                return;

            if (stage != AppUpdateStage.Downloaded)
                throw new InvalidOperationException("The update has not been downloaded.");

            filePath = QueryDownloads().First(x => x.Version == version).FilePath ??
                       throw new InvalidOperationException("The update's file is not where it was downloaded.");
            _installingVersion = version;
        }

        EnsureInstallStatusReceiver();
        var packageInstaller = packageManager.PackageInstaller;
        using var sessionParams = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
        sessionParams.SetAppPackageName(packageName);
        var sessionId = packageInstaller.CreateSession(sessionParams);
        try {
            using var session = packageInstaller.OpenSession(sessionId);
            await using (var input = File.OpenRead(filePath))
            await using (var output = session.OpenWrite("package", 0, input.Length)) {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                session.Fsync(output);
            }

            // mutable: the installer writes its answer into it
            using var statusIntent = new Intent(InstallStatusAction).SetPackage(packageName);
            var pendingIntent = PendingIntent.GetBroadcast(activity, sessionId, statusIntent,
                                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable) ??
                                throw new InvalidOperationException("Could not create the install's status intent.");
            session.Commit(pendingIntent.IntentSender);
            VhLogger.Instance.LogInformation("The update's install session is committed. Version: {Version}", version);
        }
        catch {
            lock (_lock)
                _installingVersion = null;

            packageInstaller.AbandonSession(sessionId);
            throw;
        }
    }

    // Android's own screen asks the person at once: nothing is left half done to bring back
    public Task<bool> ResumeUpdate(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public bool IsAutomaticUpdateSupported => false;

    public Task<bool> IsAutomaticUpdateOn(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The website's APK updates only from the notice.");
    }

    public Task SetAutomaticUpdate(bool isOn, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The website's APK updates only from the notice.");
    }

    internal void OnInstallStatus(Context context, Intent intent)
    {
        var status = (PackageInstallStatus)intent.GetIntExtra(PackageInstaller.ExtraStatus,
            (int)PackageInstallStatus.Failure);
        var message = intent.GetStringExtra(PackageInstaller.ExtraStatusMessage);
        VhLogger.Instance.LogInformation("The installer answered. Status: {Status}, Message: {Message}",
            status, message);

        lock (_lock) {
            var version = _installingVersion;
            _installingVersion = null;
            switch (status) {
                // Android's own screen asks the person; the app is in front, since they just pressed
                case PackageInstallStatus.PendingUserAction:
                    StartConfirmation(context, intent);
                    break;

                // installed: this process ends with the app it replaced. Or the person said no: the
                // notice keeps Install
                case PackageInstallStatus.Success:
                case PackageInstallStatus.FailureAborted:
                    break;

                // a package Android refused it refuses again: Update downloads it anew
                default:
                    if (version != null) {
                        SetFailed(version, $"The install failed. {message ?? status.ToString()}");
                        foreach (var download in QueryDownloads().Where(x => x.Version == version))
                            Remove(download);
                    }

                    break;
            }
        }
    }

    // The installer's own confirmation screen, which the status names. Only a system app's: before
    // Android 13 the receiver cannot be kept from other apps, which must not get a screen of theirs
    // started from this app.
    private static void StartConfirmation(Context context, Intent intent)
    {
        var confirmIntent = OperatingSystem.IsAndroidVersionAtLeast(33)
            ? intent.GetParcelableExtra(Intent.ExtraIntent, Java.Lang.Class.FromType(typeof(Intent))) as Intent
#pragma warning disable CA1422 // the typed overload is Android 13's
            : intent.GetParcelableExtra(Intent.ExtraIntent) as Intent;
#pragma warning restore CA1422

        var packageManager = context.PackageManager;
        var targetPackage = packageManager != null ? confirmIntent?.ResolveActivity(packageManager)?.PackageName : null;
        if (confirmIntent is null || targetPackage is null || !IsSystemApp(packageManager!, targetPackage)) {
            VhLogger.Instance.LogWarning("The installer's confirmation is not a system screen. Package: {Package}",
                targetPackage);
            return;
        }

        confirmIntent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(confirmIntent);
    }

    private void EnsureInstallStatusReceiver()
    {
        lock (_lock) {
            if (_installStatusReceiver != null)
                return;

            var receiver = new InstallStatusReceiver(this);
            using var filter = new IntentFilter(InstallStatusAction);
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
                Context.RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
            else
                Context.RegisterReceiver(receiver, filter);

            _installStatusReceiver = receiver;
        }
    }

    private void SetFailed(Version version, string message)
    {
        VhLogger.Instance.LogWarning("The update failed. Version: {Version}, Message: {Message}", version, message);
        _failedVersion = version;
        _failedMessage = message;
    }

    // https only, as the package replaces the app. A Debug build also takes a loopback http address:
    // the emulator's test feed, reached through adb reverse.
    private static bool IsAllowedPackageUrl(Uri packageUrl)
    {
#if DEBUG
        if (packageUrl.Scheme == Uri.UriSchemeHttp && packageUrl.IsLoopback)
            return true;
#endif
        return packageUrl.Scheme == Uri.UriSchemeHttps;
    }

    private static string FileNameOf(Version version) => FilePrefix + version + FileExtension;

    private static void Remove(PackageDownload download)
    {
        VhLogger.Instance.LogDebug("Removing an update's package. Version: {Version}, Id: {Id}",
            download.Version, download.Id);
        DownloadManager.Remove(download.Id);
    }

    // A package file no download names: one DownloadManager has forgotten (the person cleared its list),
    // or one it renamed beside an older file. Only this provider's names; DownloadManager's own
    // temporary files are left to it.
    private static void DeleteOtherFiles(string? keepFilePath)
    {
        var folder = new DirectoryInfo(FolderPath);
        if (!folder.Exists)
            return;

        foreach (var file in folder.GetFiles(FilePrefix + "*" + FileExtension)) {
            if (file.FullName == keepFilePath)
                continue;

            VhLogger.Instance.LogDebug("Deleting a leftover update file. File: {File}", file.Name);
            file.Delete();
        }
    }

    private static List<PackageDownload> QueryDownloads()
    {
        var downloads = new List<PackageDownload>();
        var folderPath = FolderPath;
        using var query = new DownloadManager.Query();
        using var cursor = DownloadManager.InvokeQuery(query);
        if (cursor is null)
            return downloads;

        var idIndex = cursor.GetColumnIndexOrThrow(DownloadManager.ColumnId);
        var descriptionIndex = cursor.GetColumnIndexOrThrow(DownloadManager.ColumnDescription);
        var localUriIndex = cursor.GetColumnIndexOrThrow(DownloadManager.ColumnLocalUri);
        var statusIndex = cursor.GetColumnIndexOrThrow(DownloadManager.ColumnStatus);
        var reasonIndex = cursor.GetColumnIndexOrThrow(DownloadManager.ColumnReason);
        var downloadedIndex = cursor.GetColumnIndexOrThrow(DownloadManager.ColumnBytesDownloadedSoFar);
        var totalIndex = cursor.GetColumnIndexOrThrow(DownloadManager.ColumnTotalSizeBytes);
        while (cursor.MoveToNext()) {
            // DownloadManager lists only this app's downloads, and this app downloads nothing else
            // through it. The description holds the version: it is there from the start, while the
            // file's path comes only once DownloadManager writes, so a download that waits, or failed
            // before it wrote, would otherwise go unseen
            if (!Version.TryParse(cursor.GetString(descriptionIndex), out var version))
                continue;

            var localUri = cursor.GetString(localUriIndex);
            var filePath = localUri is null ? null : AndroidUri.Parse(localUri)?.Path;
            downloads.Add(new PackageDownload {
                Id = cursor.GetLong(idIndex),
                Version = version,
                FilePath = filePath is not null && Path.GetDirectoryName(filePath) == folderPath ? filePath : null,
                Status = (DownloadStatus)cursor.GetInt(statusIndex),
                Reason = cursor.GetInt(reasonIndex),
                DownloadedBytes = cursor.GetLong(downloadedIndex),
                TotalBytes = cursor.GetLong(totalIndex)
            });
        }

        return downloads;
    }

    private static string DescribeReason(int reason)
    {
        return reason switch {
            < 1000 => $"The server answered HTTP {reason}.",
            (int)DownloadError.InsufficientSpace => "There is not enough storage.",
            (int)DownloadError.DeviceNotFound => "No storage was found.",
            (int)DownloadError.CannotResume => "It could not be resumed.",
            (int)DownloadError.HttpDataError => "The connection was lost.",
            (int)DownloadError.TooManyRedirects => "The server redirected too many times.",
            (int)DownloadError.FileError => "The file could not be written.",
            _ => $"Error {reason}."
        };
    }

    // An install may take the package unless a store installed it; then the feed's link stays.
    private static bool CalcIsSupported()
    {
        try {
            var packageManager = Context.PackageManager ??
                                 throw new InvalidOperationException("The app has no PackageManager.");
            var packageName = Context.PackageName ??
                              throw new InvalidOperationException("The app has no package name.");

            // a store that installed the app keeps it updated, and a store's rules may forbid an app
            // that updates itself (IzzyOnDroid's do, unless the person opts in)
            var installer = GetInstallerPackageName(packageManager, packageName);
            if (installer != null && installer != packageName && !IsPhoneInstaller(packageManager, installer)) {
                VhLogger.Instance.LogInformation(
                    "A store installed the app and keeps it updated; it does not update itself. Installer: {Installer}",
                    installer);
                return false;
            }

            return true;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not tell whether the app may update itself.");
            return false;
        }
    }

    private static string? GetInstallerPackageName(PackageManager packageManager, string packageName)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            return packageManager.GetInstallSourceInfo(packageName).InstallingPackageName;

#pragma warning disable CA1422 // GetInstallSourceInfo is Android 11's
        return packageManager.GetInstallerPackageName(packageName);
#pragma warning restore CA1422
    }

    // The phone's own package installer, whatever its name on that brand: the one that answers Android's
    // install intent, or a system app named for it where the intent resolves to a chooser.
    private static bool IsPhoneInstaller(PackageManager packageManager, string installer)
    {
#pragma warning disable CA1422 // the typed flags are Android 13's; ACTION_INSTALL_PACKAGE stays the installer's own
        using var intent = new Intent(Intent.ActionInstallPackage)
            .SetDataAndType(AndroidUri.Parse("content://update/update.apk"), ApkMimeType);
        var resolved = packageManager.ResolveActivity(intent, (PackageInfoFlags)0)?.ActivityInfo?.PackageName;
#pragma warning restore CA1422
        return installer == resolved ||
               (installer.Contains("packageinstaller", StringComparison.OrdinalIgnoreCase) &&
                IsSystemApp(packageManager, installer));
    }

    private static bool IsSystemApp(PackageManager packageManager, string packageName)
    {
#pragma warning disable CA1422 // the typed flags are Android 13's
        var flags = packageManager.GetApplicationInfo(packageName, (PackageInfoFlags)0).Flags;
#pragma warning restore CA1422
        return flags.HasFlag(ApplicationInfoFlags.System);
    }
}
