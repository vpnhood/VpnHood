using Android.Gms.Extensions;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Updaters;
using VpnHood.Core.Client.Devices.Android;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Net.Toolkit.Logging;
using Xamarin.Google.Android.Play.Core.AppUpdate;
using Xamarin.Google.Android.Play.Core.AppUpdate.Install.Model;

namespace VpnHood.AppLib.Stores.GooglePlay;

// Play shows its update on its own screen, where the person decides. A failure throws: the service
// treats a failed check like Play's "none", and a failed screen as one to show again.
public class GooglePlayAppUpdaterProvider : IAppUpdaterProvider
{
    private AppUpdateState _state = AppUpdateState.None;

    public bool HasOwnScreen => true;

    // Play's update flow shows over the window: without one the check waits, outside the service's
    // catch, which would answer "no update" and restart the prompt's delay
    public async Task<AppUpdateState> Check(AppRelease? feedRelease, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)(uiContext ?? throw new UiContextNotAvailableException());
        using var appUpdateManager = AppUpdateManagerFactory.Create(appUiContext.Activity);
        using var appUpdateInfo = await appUpdateManager.GetAppUpdateInfo().AsAsync<AppUpdateInfo>();

        // Play answers "not available" also with no connection to Google: the service tells the two
        // apart by the connect
        VhLogger.Instance.LogDebug("Checking for Google Play update availability...");
        var updateAvailability = appUpdateInfo.UpdateAvailability();
        if (updateAvailability != UpdateAvailability.UpdateAvailable) {
            VhLogger.Instance.LogDebug(
                "Google Play update is not available. UpdateAvailability: {UpdateAvailability}",
                updateAvailability);
            _state = AppUpdateState.None;
            return _state;
        }

        // the days this phone's Play has known the update: its own update has had that long
        var stalenessDays = appUpdateInfo.ClientVersionStalenessDays()?.IntValue();
        VhLogger.Instance.LogDebug("Google Play update is available. StalenessDays: {StalenessDays}", stalenessDays);
        _state = new AppUpdateState {
            Stage = AppUpdateStage.Available,
            AvailableSince = stalenessDays is null ? null : DateTime.UtcNow.AddDays(-stalenessDays.Value)
        };
        return _state;
    }

    public AppUpdateState GetState(AppRelease? feedRelease)
    {
        return _state;
    }

    public async Task StartUpdate(AppRelease? feedRelease, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)(uiContext ?? throw new UiContextNotAvailableException());
        if (!await TryStartUpdateFlow(appUiContext, UpdateAvailability.UpdateAvailable, cancellationToken)
                .ConfigureAwait(false))
            throw new InvalidOperationException("Google Play has no update to show.");
    }

    // Play installs on its own screen: nothing waits for the app's Install
    public Task CompleteUpdate(AppRelease? feedRelease, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Google Play installs its update on its own screen.");
    }

    // Google's guide for immediate updates: one the person started and left before it finished reports
    // DeveloperTriggeredUpdateInProgress, and the app brings its screen back
    public Task<bool> ResumeUpdate(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return TryStartUpdateFlow((AndroidUiContext)uiContext,
            UpdateAvailability.DeveloperTriggeredUpdateInProgress, cancellationToken);
    }

    // Play's immediate update screen for an update in that availability; false where Play has none
    private static async Task<bool> TryStartUpdateFlow(AndroidUiContext appUiContext, int availability,
        CancellationToken cancellationToken)
    {
        using var appUpdateManager = AppUpdateManagerFactory.Create(appUiContext.Activity);
        using var appUpdateInfo = await appUpdateManager.GetAppUpdateInfo().AsAsync<AppUpdateInfo>();
        var updateAvailability = appUpdateInfo.UpdateAvailability();
        if (updateAvailability != availability) {
            VhLogger.Instance.LogDebug(
                "Google Play has no update to show. UpdateAvailability: {UpdateAvailability}",
                updateAvailability);
            return false;
        }

        // check is update type allowed (this needs to publish by api to set)
        //if (!appUpdateInfo.IsUpdateTypeAllowed(AppUpdateType.Immediate)) {
        //    VhLogger.Instance.LogDebug("Google Play immediate update is not allowed.");
        //    return false;
        //}

        // Show Google Play update dialog. Start the flow on the UI thread, then await the Play task
        // here so completion, cancellation and failures flow back to this method (an async lambda
        // would run as async-void: unawaited and crashing the UI thread on error).
        VhLogger.Instance.LogDebug("Google Play update is available, starting update flow...");

        // flexible update requires much more handling
        // ReSharper disable AccessToDisposedClosure
        var updateFlowPlayTask = await AndroidUtils.RunOnUiThread(appUiContext.Activity, () =>
            appUpdateManager.StartUpdateFlow(appUpdateInfo, appUiContext.Activity,
                AppUpdateOptions.NewBuilder(AppUpdateType.Immediate).Build())).ConfigureAwait(false);
        // ReSharper restore AccessToDisposedClosure

        if (updateFlowPlayTask != null)
            await updateFlowPlayTask.AsAsync().WaitAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }
}