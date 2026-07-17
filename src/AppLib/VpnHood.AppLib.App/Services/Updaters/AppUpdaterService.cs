using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Updaters;

public class AppUpdaterService : IDisposable
{
    private readonly AsyncLock _versionCheckLock = new();
    private readonly Version _appVersion;
    private readonly AppUpdaterOptions _updateOptions;
    private string VersionCheckFilePath => Path.Combine(field, "version.json");
    private readonly Lazy<AppUpdaterData> _lazyData;
    private AppUpdaterData Data => _lazyData.Value;
    private bool _disposed;

    public AppUpdaterService(string storageFolderPath, Version appVersion,
        AppUpdaterOptions updateOptions)
    {
        VersionCheckFilePath = storageFolderPath;
        _appVersion = appVersion;
        _updateOptions = updateOptions;
        // defer reading version.json until update status is first queried
        _lazyData = new Lazy<AppUpdaterData>(() =>
            JsonUtils.TryDeserializeFile<AppUpdaterData>(VersionCheckFilePath) ?? new AppUpdaterData());
    }

    public AppUpdaterStatus Status {
        get {
            var versionStatus = CalcVersionStatus();
            return new AppUpdaterStatus {
                VersionStatus = versionStatus,
                CheckedTime = Data.CheckedTime,
                PublishInfo = Data.PublishInfo,
                Prompt = Data.UpdaterAvailableSince == null &&
                         versionStatus is VersionStatus.Old or VersionStatus.Deprecated &&
                         !IsInPostponeTime
            };
        }
    }

    private bool IsInPostponeTime =>
        Data.PostponeTime != null &&
        DateTime.Now - Data.PostponeTime < _updateOptions.PostponePeriod &&
        _appVersion.Equals(Data.PostponeVersion);

    private VersionStatus CalcVersionStatus()
    {
        if (Data.UpdaterAvailableSince != null)
            return VersionStatus.Old;

        if (Data.PublishInfo == null)
            return VersionStatus.Unknown;

        // wait for updater
        if (DateTime.UtcNow - Data.PublishInfo.ReleaseDate < Data.PublishInfo.NotificationDelay)
            return VersionStatus.Latest; // assume the latest version is available to let store validate the app

        // set default notification delay
        if (_appVersion <= Data.PublishInfo.DeprecatedVersion)
            return VersionStatus.Deprecated;

        return _appVersion < Data.PublishInfo.Version
            ? VersionStatus.Old
            : VersionStatus.Latest;
    }

    public async Task TryCheckForUpdate(bool force, CancellationToken cancellationToken)
    {
        try {
            await CheckForUpdate(force, cancellationToken).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error occurred while checking for updates.");
            // Handle the error as needed, e.g., log it or notify the user
        }
    }

    public async Task CheckForUpdate(bool force, CancellationToken cancellationToken)
    {
        // check if the version check is already in progress
        using var lockAsync = await _versionCheckLock.LockAsync(cancellationToken).Vhc();

        // reset postpone time and version if forced
        if (force) {
            Data.PostponeTime = null; // reset postpone time if forced
            Data.PostponeVersion = null; // reset postpone version if forced
        }

        // check ui context
        VhLogger.Instance.LogDebug("VersionCheck requested. Force: {Force}", force);
        if (AppUiContext.Context == null) {
            VhLogger.Instance.LogDebug("VersionCheck is not available. AppUiContext is null.");
            return;
        }

        // check if any update info URL or updater provider is available
        if (_updateOptions.UpdateInfoUrl is null && _updateOptions.UpdaterProvider is null) {
            VhLogger.Instance.LogDebug("VersionCheck is not available. No update info URL or updater provider.");
            return;
        }

        // skip if already checked and not forced
        if (!force && DateTime.Now - Data.CheckedTime < _updateOptions.CheckInterval) {
            VhLogger.Instance.LogDebug("VersionCheck is postponed. CheckedTime: {CheckedTime}, Interval: {Interval}",
                Data.CheckedTime, _updateOptions.CheckInterval);
            return;
        }

        try {
            // check by provider
            if (await TryUpdateByProvider(force, cancellationToken) ||
                await TryUpdateByPublishInfoUrl(cancellationToken)) {
                Data.CheckedTime = DateTime.Now;
            }
        }
        finally {
            Save();
        }
    }

    private async Task<bool> TryUpdateByProvider(bool force, CancellationToken cancellationToken)
    {
        try {
            var updater = _updateOptions.UpdaterProvider;
            if (updater == null)
                return false; // no updater provider. Job done

            if (!await updater.IsUpdateAvailable(AppUiContext.RequiredContext, cancellationToken)) {
                Data.UpdaterAvailableSince = null;
                return false; // no update available or could not handle the update
            }

            // update available time if not set
            Data.UpdaterAvailableSince ??= DateTime.Now;

            // check if the update is available for the given delay
            if (!force && !IsInPostponeTime &&
                DateTime.Now - Data.UpdaterAvailableSince < _updateOptions.PromptDelay)
                return true; // handled

            // update available, try to update
            if (!await updater.Update(AppUiContext.RequiredContext, cancellationToken).Vhc())
                return false; // handled

            // update was handled so reset the UpdaterAvailableSince, till next check
            Data.UpdaterAvailableSince = null;
            Postpone(); // this version is postponed because user either updated or skipped the update
            return true;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not check version by UpdaterProvider.");
            return false; // could not check version by UpdaterProvider. 
        }
    }


    // ReSharper disable once UnusedMethodReturnValue.Local
    private async Task<bool> TryUpdateByPublishInfoUrl(CancellationToken cancellationToken)
    {
        try {
            var updateInfoUrl = _updateOptions.UpdateInfoUrl;
            if (updateInfoUrl == null)
                return false; // no update info url. Job done

            // fetch the latest publish info
            VhLogger.Instance.LogDebug("Retrieving the latest publish info...");
            // ReSharper disable once ShortLivedHttpClient
            using var httpClient = new HttpClient();
            var publishInfoJson = await httpClient.GetStringAsync(updateInfoUrl, cancellationToken).Vhc();
            Data.PublishInfo = JsonUtils.Deserialize<PublishInfo>(publishInfoJson);

            VhLogger.Instance.LogInformation(
                "The latest publish info has been retrieved. VersionStatus: {VersionStatus}, LatestVersion: {LatestVersion}",
                CalcVersionStatus(), Data.PublishInfo.Version);

            return true; // Job done
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning("Could not retrieve the latest publish info information. Error: {Error}",
                ex.Message);
            return false; // could not retrieve the latest publish info. try later
        }
    }

    public void Postpone()
    {
        // version status is unknown when the app container can do it
        Data.PostponeTime = DateTime.Now;
        Data.PostponeVersion = _appVersion;
        Save();
    }

    private void Save()
    {
        // It looks like TryCheckForUpdate could not handle exceptions in .NET 10 background tasks
        // The folder does not exist in test finalizer and test host crash so we try not to save if disposed
        if (!_disposed)
            File.WriteAllText(VersionCheckFilePath, JsonSerializer.Serialize(Data));
    }

    public void Dispose()
    {
        _disposed = true;
    }
}