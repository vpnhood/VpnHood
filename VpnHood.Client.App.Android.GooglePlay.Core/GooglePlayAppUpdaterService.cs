using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Abstractions;
using VpnHood.Common.Logging;
using Xamarin.Google.Android.Play.Core.AppUpdate;
using Xamarin.Google.Android.Play.Core.Install;
using Xamarin.Google.Android.Play.Core.Install.Model;

namespace VpnHood.Client.App.Droid.GooglePlay;

public class GooglePlayAppUpdaterService(Activity activity) : IAppUpdaterService
{
    private readonly TaskCompletionSource<int> _taskCompletionSource = new();
    public async Task<bool> Update()
    {
        var appUpdateManager =  AppUpdateManagerFactory.Create(activity);
        try
        {
            var appUpdateInfo = await new GooglePlayTaskCompleteListener<AppUpdateInfo>(appUpdateManager.AppUpdateInfo).Task;
            var updateAvailability = appUpdateInfo.UpdateAvailability();

            // play set UpdateAvailability.UpdateNotAvailable even when there is no connection to google
            // So we return false if there is UpdateNotAvailable to let the alternative way works
            if (updateAvailability != UpdateAvailability.UpdateAvailable || !appUpdateInfo.IsUpdateTypeAllowed(AppUpdateType.Flexible))
                return false;

            // Show Google Play update dialog
            var updateFlowPlayTask = appUpdateManager.StartUpdateFlow(appUpdateInfo, activity, AppUpdateOptions.NewBuilder(AppUpdateType.Flexible).Build());
            var updateFlowResult = await new GooglePlayTaskCompleteListener<Java.Lang.Integer>(updateFlowPlayTask).Task;
            if (updateFlowResult.IntValue() != -1)
                throw new Exception("Could not start update flow.");

            // Set listener to check download state
            appUpdateManager.RegisterListener(new GooglePlayDownloadStateListener(_taskCompletionSource));
            var downloadState = await _taskCompletionSource.Task;

            // Download failed
            if (downloadState != InstallStatus.Downloaded)
                return false;

            // Start install downloaded update
            var installUpdateTask = appUpdateManager.CompleteUpdate();
            var installUpdateStatus = await new GooglePlayTaskCompleteListener<Java.Lang.Integer>(installUpdateTask).Task;

            // Could not start install
            if (installUpdateStatus.IntValue() != -1)
                throw new Exception("Could not complete update.");

            // Set listener to check installation state
            var installState = await _taskCompletionSource.Task;
            // TODO Check for unregister
            //appUpdateManager.UnregisterListener(GooglePlayDownloadStateListener);
            appUpdateManager.Dispose();

            return installState == InstallStatus.Installed;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(ex, "Could not check for new version.");
            return false;
        }
    }

    public class GooglePlayDownloadStateListener(TaskCompletionSource<int> taskCompletionSource) : Java.Lang.Object, IInstallStateUpdatedListener
    {
        public void OnStateUpdate(InstallState state)
        {
            var status = state.InstallStatus();
            switch (status)
            {
                case InstallStatus.Downloaded:
                    taskCompletionSource.TrySetResult(status);
                    break;
                case InstallStatus.Installed:
                    taskCompletionSource.TrySetResult(status);
                    break;
                case InstallStatus.Canceled:
                    taskCompletionSource.SetCanceled();
                    break;
                case InstallStatus.Failed:
                    taskCompletionSource.TrySetException(new Exception("Download Failed."));
                    break;
                case InstallStatus.Unknown:
                    taskCompletionSource.TrySetException(new Exception("Unknown status."));
                    break;
            }
        }
    }
}