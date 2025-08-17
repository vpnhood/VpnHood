using Microsoft.Extensions.Logging;
using System.Text.Json;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Updaters;

public class AppUpdaterService
{
    private readonly AsyncLock _versionCheckLock = new();
    private readonly string _storageFolderPath;
    private readonly Version _appVersion;
    private readonly AppUpdaterOptions _updateOptions;
    private string VersionCheckFilePath => Path.Combine(_storageFolderPath, "version.json");
    private readonly AppUpdaterData _data;

    public AppUpdaterService(string storageFolderPath, Version appVersion,
        AppUpdaterOptions updateOptions)
    {
        _storageFolderPath = storageFolderPath;
        _appVersion = appVersion;
        _updateOptions = updateOptions;
        _data = JsonUtils.TryDeserializeFile<AppUpdaterData>(VersionCheckFilePath) ??
                              new AppUpdaterData();
    }

    public AppUpdaterStatus Status {
        get {
            var versionStatus = CalcVersionStatus();
            return new AppUpdaterStatus {
                VersionStatus = versionStatus,
                CheckedTime = _data.CheckedTime,
                PublishInfo = _data.PublishInfo,
                Prompt = _data.UpdaterAvailableSince == null &&
                         versionStatus is VersionStatus.Old or VersionStatus.Deprecated &&
                         !IsInPostponeTime
            };
        }
    }

    private bool IsInPostponeTime =>
        _data.PostponeTime != null &&
        DateTime.Now - _data.PostponeTime < _updateOptions.PostponePeriod &&
        _appVersion.Equals(_data.PostponeVersion);

    private VersionStatus CalcVersionStatus()
    {
        if (_data.UpdaterAvailableSince != null)
            return VersionStatus.Old;

        if (_data.PublishInfo == null)
            return VersionStatus.Unknown;

        // wait for updater
        if(DateTime.UtcNow - _data.PublishInfo.ReleaseDate < _data.PublishInfo.NotificationDelay)
            return VersionStatus.Latest; // assume the latest version is available to let store validate the app

        // set default notification delay
        if (_appVersion <= _data.PublishInfo.DeprecatedVersion)
            return VersionStatus.Deprecated;

        return _appVersion < _data.PublishInfo.Version
            ? VersionStatus.Old
            : VersionStatus.Latest;
    }

    public async Task CheckForUpdate(bool force, CancellationToken cancellationToken)
    {
        // check if the version check is already in progress
        using var lockAsync = await _versionCheckLock.LockAsync(cancellationToken).Vhc();

        // reset postpone time and version if forced
        if (force) {
            _data.PostponeTime = null; // reset postpone time if forced
            _data.PostponeVersion = null; // reset postpone version if forced
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
        if (!force && _data.CheckedTime + _updateOptions.CheckInterval > DateTime.Now) {
            VhLogger.Instance.LogDebug("VersionCheck is postponed. CheckedTime: {CheckedTime}, Interval: {Interval}",
                _data.CheckedTime, _updateOptions.CheckInterval);
            return;
        }

        try {
            // check by provider
            if (await TryUpdateByProvider(force, cancellationToken) ||
                await TryUpdateByPublishInfoUrl(cancellationToken)) {
                _data.CheckedTime = DateTime.Now;
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
                _data.UpdaterAvailableSince = null;
                return false; // no update available or could not handle the update
            }

            // update available time if not set
            _data.UpdaterAvailableSince ??= DateTime.Now;

            // check if the update is available for the given delay
            if (!force && !IsInPostponeTime && 
                DateTime.Now - _data.UpdaterAvailableSince < _updateOptions.PromptDelay)
                return true; // handled

            // update available, try to update
            if (!await updater.Update(AppUiContext.RequiredContext, cancellationToken).Vhc())
                return false; // handled

            // update was handled so reset the UpdaterAvailableSince, till next check
            _data.UpdaterAvailableSince = null;
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
            using var httpClient = new HttpClient();
            var publishInfoJson = await httpClient.GetStringAsync(updateInfoUrl, cancellationToken).Vhc();
            _data.PublishInfo = JsonUtils.Deserialize<PublishInfo>(publishInfoJson);

            VhLogger.Instance.LogInformation(
                "The latest publish info has been retrieved. VersionStatus: {VersionStatus}, LatestVersion: {LatestVersion}",
                CalcVersionStatus(), _data.PublishInfo.Version);

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
        _data.PostponeTime = DateTime.Now;
        _data.PostponeVersion = _appVersion;
        Save();
    }

    private void Save()
    {
        File.WriteAllText(VersionCheckFilePath, JsonSerializer.Serialize(_data));
    }
}