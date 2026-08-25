using Android.Gms.Extensions;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Devices.Droid;
using VpnHood.Core.Client.Devices.Droid.Utils;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Logging;
using Xamarin.Google.Android.Play.Core.AppUpdate;
using Xamarin.Google.Android.Play.Core.AppUpdate.Install.Model;

namespace VpnHood.AppLib.Droid.GooglePlay;

public class GooglePlayAppUpdaterProvider : IAppUpdaterProvider
{
    public Task<bool> IsUpdateAvailable(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return UpdateInternal(uiContext, false, cancellationToken);
    }

    public Task<bool> Update(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return UpdateInternal(uiContext, true, cancellationToken);
    }

    public static async Task<bool> UpdateInternal(IUiContext uiContext, bool execute, CancellationToken cancellationToken)
    {
        try {
            var appUiContext = (AndroidUiContext)uiContext;
            using var appUpdateManager = AppUpdateManagerFactory.Create(appUiContext.Activity);
            using var appUpdateInfo = await appUpdateManager.GetAppUpdateInfo().AsAsync<AppUpdateInfo>();

            // play set UpdateAvailability.UpdateNotAvailable even when there is no connection to google
            // So we return false if there is UpdateNotAvailable to let the alternative way works
            VhLogger.Instance.LogDebug("Checking for Google Play update availability...");
            var updateAvailability = appUpdateInfo.UpdateAvailability();
            if (updateAvailability != UpdateAvailability.UpdateAvailable) {
                VhLogger.Instance.LogDebug(
                    "Google Play update is not available. UpdateAvailability: {UpdateAvailability}",
                    updateAvailability);
                return false;
            }

            // just return if execute is not required
            if (!execute)
                return true;

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
        catch (Exception ex) {
            // return false to allow the alternative way
            // google play does not throw exception if user cancel exception
            VhLogger.Instance.LogWarning(ex, "Could not update the app using Google Play.");
            return false;
        }
    }
}