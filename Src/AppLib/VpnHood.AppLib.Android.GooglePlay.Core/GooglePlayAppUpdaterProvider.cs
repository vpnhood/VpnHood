using Android.Gms.Extensions;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device.Droid;
using VpnHood.Core.Client.Device.Droid.Utils;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Toolkit.Logging;
using Xamarin.Google.Android.Play.Core.AppUpdate;
using Xamarin.Google.Android.Play.Core.AppUpdate.Install.Model;

namespace VpnHood.AppLib.Droid.GooglePlay;

public class GooglePlayAppUpdaterProvider : IAppUpdaterProvider
{
    public async Task<bool> Update(IUiContext uiContext, CancellationToken cancellationToken)
    {
        try {
            var appUiContext = (AndroidUiContext)uiContext;
            using var appUpdateManager = AppUpdateManagerFactory.Create(appUiContext.Activity);

            using var appUpdateInfo =
                await appUpdateManager.GetAppUpdateInfo().AsAsync<AppUpdateInfo>();
            //throw new Exception("Could not retrieve AppUpdateInfo");

            // play set UpdateAvailability.UpdateNotAvailable even when there is no connection to google
            // So we return false if there is UpdateNotAvailable to let the alternative way works
            var updateAvailability = appUpdateInfo.UpdateAvailability();
            if (updateAvailability != UpdateAvailability.UpdateAvailable ||
                !appUpdateInfo.IsUpdateTypeAllowed(AppUpdateType.Immediate))
                return false;

            // Show Google Play update dialog
            await AndroidUtil.RunOnUiThread(appUiContext.Activity, async () => {
                using var updateFlowPlayTask = appUpdateManager.StartUpdateFlow(appUpdateInfo, appUiContext.Activity,
                    AppUpdateOptions.NewBuilder(AppUpdateType.Immediate).Build());
                if (updateFlowPlayTask != null)
                    await updateFlowPlayTask.AsAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            });

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