using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Accounts;
using VpnHood.Common.Logging;
using Xamarin.Google.Android.Play.Core.AppUpdate;
using Xamarin.Google.Android.Play.Core.Install.Model;

namespace VpnHood.Client.App.Droid.GooglePlay;

public class GooglePlayAppUpdaterService(Activity activity): IAppUpdaterService
{
    public async Task<bool> Update()
    {
        var appUpdateManager = AppUpdateManagerFactory.Create(activity);
        try
        {
            var appUpdateInfo = await new GooglePlayTaskCompleteListener<AppUpdateInfo>(appUpdateManager.AppUpdateInfo).Task;
            var updateAvailability = appUpdateInfo.UpdateAvailability();

            // postpone check if check succeeded
            if (updateAvailability == UpdateAvailability.UpdateAvailable &&
                appUpdateInfo.IsUpdateTypeAllowed(AppUpdateType.Flexible))
            {
                appUpdateManager.StartUpdateFlowForResult(
                    appUpdateInfo, activity, AppUpdateOptions.NewBuilder(AppUpdateType.Flexible).Build(), 0);
                return true;
            }

            // play set UpdateAvailability.UpdateNotAvailable even when there is no connection to google
            // So we return false if there is UpdateNotAvailable to let the alternative way works
            return false;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(ex, "Could not check for new version.");
            return false;
        }
    }


}