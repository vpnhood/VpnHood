using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.Updaters;
using VpnHood.AppLib.App.DtoConverters;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;
// the provider's update types share their names with the contract's, which this file speaks
using ProviderUpdaters = VpnHood.AppLib.Abstractions.Updaters;

namespace VpnHood.AppLib.App.Services.Updaters;

public class AppUpdaterService : IDisposable
{
    private readonly AsyncLock _versionCheckLock = new();
    private readonly Version _appVersion;
    private readonly AppUpdaterOptions _updateOptions;
    private readonly Func<bool> _isConnected;
    private string VersionCheckFilePath => Path.Combine(field, "version.json");
    private readonly Lazy<AppUpdaterData> _lazyData;
    private AppUpdaterData Data => _lazyData.Value;
    private string? _lastUpdateStateError;
    private bool _disposed;

    /// <param name="isConnected">
    /// Whether the VPN is connected: a filtered network may reach the store only through the tunnel,
    /// so the store's "none" counts only then.
    /// </param>
    public AppUpdaterService(string storageFolderPath, Version appVersion,
        AppUpdaterOptions updateOptions, Func<bool> isConnected)
    {
        VersionCheckFilePath = storageFolderPath;
        _appVersion = appVersion;
        _updateOptions = updateOptions;
        _isConnected = isConnected;
        // defer reading version.json until update status is first queried
        _lazyData = new Lazy<AppUpdaterData>(() => {
            var data = JsonUtils.TryDeserializeFile<AppUpdaterData>(VersionCheckFilePath) ?? new AppUpdaterData();

            // these stamps used to be written in local time and deserialize as local kind; the
            // comparisons are in UTC now, so normalize once (a no-op for values already in UTC)
            data.PostponeTime = data.PostponeTime?.ToUniversalTime();
            data.CheckedTime = data.CheckedTime?.ToUniversalTime();
            data.UpdaterAvailableSince = data.UpdaterAvailableSince?.ToUniversalTime();
            return data;
        });
    }

    public AppUpdaterStatus Status {
        get {
            var versionStatus = CalcVersionStatus();
            var updateState = GetUpdateState();
            return new AppUpdaterStatus {
                VersionStatus = versionStatus,
                CheckedTime = Data.CheckedTime,
                PublishInfo = Data.PublishInfo,
                // the feed's notice is the way only where a store's own screen is not; an update the
                // person started keeps it, whatever Later: their own choice, not a prompt
                Prompt = updateState?.Stage is AppUpdateStage.Downloading or AppUpdateStage.Downloaded or
                             AppUpdateStage.Installing or AppUpdateStage.Failed ||
                         (!IsUpdaterOffering &&
                          versionStatus is VersionStatus.Old or VersionStatus.Deprecated &&
                          !IsInPostponeTime),
                UpdateState = updateState
            };
        }
    }

    // The feed's release where it is newer than this version: the only one worth keeping
    private ProviderUpdaters.AppRelease? NewerRelease =>
        Data.PublishInfo is { } publishInfo && publishInfo.Version > _appVersion
            ? new ProviderUpdaters.AppRelease { Version = publishInfo.Version, PackageUrl = publishInfo.PackageUrl }
            : null;

    // The updater whose update the notice offers: one with no screen of its own
    private ProviderUpdaters.IAppUpdaterProvider? NoticeUpdater =>
        _updateOptions.UpdaterProvider is { HasOwnScreen: false } updater ? updater : null;

    // Asked on every read, which is what removes what another release left: an older one's, or the one
    // just installed. An updater that fails says so in the notice, never in the whole state.
    private AppUpdateState? GetUpdateState()
    {
        try {
            var updater = NoticeUpdater;
            if (updater is null)
                return null;

            var release = NewerRelease;
            var state = updater.GetState(release);
            _lastUpdateStateError = null;
            return release is null ? null : state.ToAppDto(release.Version);
        }
        catch (Exception ex) {
            // read every second by the UI: logged once per error, not once per read
            if (_lastUpdateStateError != ex.Message)
                VhLogger.Instance.LogError(ex, "Could not read the update's state.");

            _lastUpdateStateError = ex.Message;
            return NewerRelease is { } release
                ? new AppUpdateState {
                    Version = release.Version,
                    Stage = AppUpdateStage.Failed,
                    DownloadedBytes = 0,
                    TotalBytes = null,
                    ErrorMessage = ex.Message
                }
                : null;
        }
    }

    // The notice's Update: the newer release's update, started the updater's own way
    public Task StartUpdate(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        var updater = NoticeUpdater ?? throw new NotSupportedException("The notice offers no update of its own.");
        var release = NewerRelease;
        VhLogger.Instance.LogInformation("Starting the update. Version: {Version}", release?.Version);
        return updater.StartUpdate(release, uiContext, cancellationToken);
    }

    // The notice's Install: the downloaded update, handed to the platform's installer
    public Task CompleteUpdate(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        var updater = NoticeUpdater ?? throw new NotSupportedException("The notice offers no update of its own.");
        var release = NewerRelease;
        VhLogger.Instance.LogInformation("Completing the update. Version: {Version}", release?.Version);
        return updater.CompleteUpdate(release, uiContext, cancellationToken);
    }

    // The store said lately that it has an update for the installed version: its own screen is the way.
    // An offer to the version before an install, or from before the version was kept with it, is none.
    private bool IsUpdaterOffering =>
        _appVersion.Equals(Data.UpdaterAvailableVersion) &&
        FastDateTime.UtcNow - Data.UpdaterLastAvailableTime < _updateOptions.UpdaterOfferPeriod;

    private bool IsInPostponeTime =>
        Data.PostponeTime != null &&
        FastDateTime.UtcNow - Data.PostponeTime < _updateOptions.PostponePeriod &&
        _appVersion.Equals(Data.PostponeVersion);

    private VersionStatus CalcVersionStatus()
    {
        if (IsUpdaterOffering)
            return VersionStatus.Old;

        if (Data.PublishInfo == null)
            return VersionStatus.Unknown;

        // wait for updater
        if (FastDateTime.UtcNow - Data.PublishInfo.ReleaseDate < Data.PublishInfo.NotificationDelay)
            return VersionStatus.Latest; // assume the latest version is available to let store validate the app

        // set default notification delay
        if (_appVersion <= Data.PublishInfo.DeprecatedVersion)
            return VersionStatus.Deprecated;

        return _appVersion < Data.PublishInfo.Version
            ? VersionStatus.Old
            : VersionStatus.Latest;
    }

    public async Task TryCheckForUpdate(IUiContext? uiContext, bool force, CancellationToken cancellationToken)
    {
        try {
            await CheckForUpdate(uiContext, force, cancellationToken).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error occurred while checking for updates.");
            // Handle the error as needed, e.g., log it or notify the user
        }
    }

    /// <param name="uiContext">
    /// The window, or null where none is open, as in the desktop service: the feed needs none, and a
    /// store's updater that needs one leaves the check for a call that has it.
    /// </param>
    /// <returns>What the check found, which a check the person asked for tells them.</returns>
    public async Task<VersionCheckResult> CheckForUpdate(IUiContext? uiContext, bool force, CancellationToken cancellationToken)
    {
        // check if the version check is already in progress
        using var lockAsync = await _versionCheckLock.LockAsync(cancellationToken).Vhc();

        // an update the person started and left before it finished comes back with a window in front,
        // whatever the interval or a postpone: their own choice, not a prompt
        if (await TryResumeUpdate(uiContext, cancellationToken).Vhc())
            return VersionCheckResult.UpdateAvailable;

        // reset postpone time and version if forced
        if (force) {
            Data.PostponeTime = null; // reset postpone time if forced
            Data.PostponeVersion = null; // reset postpone version if forced
        }

        VhLogger.Instance.LogDebug("VersionCheck requested. Force: {Force}", force);

        // check if any update info URL or updater provider is available
        if (_updateOptions.UpdateInfoUrl is null && _updateOptions.UpdaterProvider is null) {
            VhLogger.Instance.LogDebug("VersionCheck is not available. No update info URL or updater provider.");
            return VersionCheckResult.Failed;
        }

        // skip if already checked and not forced
        if (!force && FastDateTime.UtcNow - Data.CheckedTime < _updateOptions.CheckInterval) {
            VhLogger.Instance.LogDebug("VersionCheck is postponed. CheckedTime: {CheckedTime}, Interval: {Interval}",
                Data.CheckedTime, _updateOptions.CheckInterval);
            return GetCheckResult(isAnswered: true);
        }

        try {
            // no store: the feed, and the app's own updater, if any, takes its release from it. Its
            // answer counts with no connect, so the feed's read stamps the check
            var updater = _updateOptions.UpdaterProvider;
            if (updater is not { HasOwnScreen: true }) {
                var isFeedRead = await TryUpdateByPublishInfoUrl(cancellationToken).Vhc();
                if (isFeedRead || _updateOptions.UpdateInfoUrl == null)
                    Data.CheckedTime = FastDateTime.UtcNow;

                if (updater != null)
                    await updater.Check(NewerRelease, uiContext, cancellationToken).Vhc();

                return GetCheckResult(isAnswered: isFeedRead);
            }

            // the store's updater first, then the feed. The store's "none" before a connect leaves the
            // check unstamped, whatever the feed said, so the try after the connect - through the
            // tunnel - asks the store again
            var storeAnswer = await TryUpdateByStore(updater, uiContext, force, cancellationToken).Vhc();
            var hasStoreUpdate = storeAnswer is StoreAnswer.Offered or StoreAnswer.ScreenFailed;
            var isPublishInfoRead = !hasStoreUpdate && await TryUpdateByPublishInfoUrl(cancellationToken).Vhc();
            if (hasStoreUpdate ||
                (storeAnswer == StoreAnswer.None && (isPublishInfoRead || _updateOptions.UpdateInfoUrl == null)))
                Data.CheckedTime = FastDateTime.UtcNow;

            return storeAnswer switch {
                StoreAnswer.Offered => VersionCheckResult.UpdateAvailable,
                StoreAnswer.ScreenFailed => VersionCheckResult.Failed,
                _ => GetCheckResult(isAnswered: isPublishInfoRead || storeAnswer == StoreAnswer.None)
            };
        }
        catch (UiContextNotAvailableException) {
            // A store's updater shows its own screen: the check waits for a window in front, unstamped,
            // or the feed's answer would put the store's prompt off for a whole interval
            VhLogger.Instance.LogDebug("The updater provider needs a window in front, and none is.");
            return VersionCheckResult.Failed;
        }
        finally {
            Save();
        }
    }

    // What a check tells the person who asked: the update, which the store's screen or the feed's
    // notice shows; nothing newer; or no answer - no source replied, or the store that has the update
    // is out of reach now, its "none" being no answer
    private VersionCheckResult GetCheckResult(bool isAnswered)
    {
        var status = Status;
        if (status.Prompt)
            return VersionCheckResult.UpdateAvailable;

        if (!isAnswered || IsUpdaterOffering)
            return VersionCheckResult.Failed;

        return status.VersionStatus is VersionStatus.Old or VersionStatus.Deprecated
            ? VersionCheckResult.UpdateAvailable
            : VersionCheckResult.Latest;
    }

    private async Task<bool> TryResumeUpdate(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        try {
            var updater = _updateOptions.UpdaterProvider;
            return updater != null && uiContext != null && await uiContext.IsActive().Vhc() &&
                   await updater.ResumeUpdate(uiContext, cancellationToken).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not resume the update the user started.");
            return false;
        }
    }

    // What the store's updater made of a check
    private enum StoreAnswer
    {
        // it has no update to offer, and the feed decides
        None,

        // it said "none" before a connect, which may only mean it could not reach its servers - Play
        // answers "none" with no connection to Google, and a filtered network reaches the store only
        // through the tunnel - so the try after the connect asks it again
        Unconfirmed,

        // it has the update in hand: shown, waiting its turn, or put off
        Offered,

        // it has the update, and its screen failed: shown again at the next check, since the store's
        // update stays the way while the store offers it
        ScreenFailed
    }

    private async Task<StoreAnswer> TryUpdateByStore(ProviderUpdaters.IAppUpdaterProvider store, IUiContext? uiContext,
        bool force, CancellationToken cancellationToken)
    {
        ProviderUpdaters.AppUpdateState state;
        try {
            state = await store.Check(NewerRelease, uiContext, cancellationToken).Vhc();
        }
        catch (UiContextNotAvailableException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not check version by the store's updater.");
            return _isConnected() ? StoreAnswer.None : StoreAnswer.Unconfirmed;
        }

        if (state.Stage == ProviderUpdaters.AppUpdateStage.None)
            return _isConnected() ? StoreAnswer.None : StoreAnswer.Unconfirmed;

        // an offer belongs to the version it was made to: an install starts a new one
        var now = FastDateTime.UtcNow;
        if (!_appVersion.Equals(Data.UpdaterAvailableVersion)) {
            Data.UpdaterAvailableVersion = _appVersion;
            Data.UpdaterAvailableSince = null;
        }

        // the store's own date where it names one (Play: the day it learned of the update), else the
        // first offer seen here
        Data.UpdaterAvailableSince = state.AvailableSince ?? Data.UpdaterAvailableSince ?? now;
        Data.UpdaterLastAvailableTime = now;

        // wait while the user's postpone is active or the store's own update has not had its chance yet
        if (!force && (IsInPostponeTime || now - Data.UpdaterAvailableSince < _updateOptions.PromptDelay))
            return StoreAnswer.Offered;

        // the store's screen shows over the window, and Android opens none for an app in the
        // background: a window there leaves the check for its return to the front
        if (uiContext == null || !await uiContext.IsActive().Vhc())
            throw new UiContextNotAvailableException();

        try {
            await store.StartUpdate(NewerRelease, uiContext, cancellationToken).Vhc();
        }
        catch (UiContextNotAvailableException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "The store's update screen failed.");
            return StoreAnswer.ScreenFailed;
        }

        // shown: the user updated or put it off, and the store's next offer waits for Later's period,
        // then for PromptDelay where the store names no date of its own
        Data.UpdaterAvailableSince = null;
        MarkPostponed();
        return StoreAnswer.Offered;
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

    // Later on the feed's notice: under the check's lock, as both write version.json
    public async Task Postpone(CancellationToken cancellationToken)
    {
        using var lockAsync = await _versionCheckLock.LockAsync(cancellationToken).Vhc();
        MarkPostponed();
        Save();
    }

    // the caller holds the check's lock
    private void MarkPostponed()
    {
        Data.PostponeTime = FastDateTime.UtcNow;
        Data.PostponeVersion = _appVersion;
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