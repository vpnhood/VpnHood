using Java.Lang;
using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Droid;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using Xamarin.Google.Android.Play.Core.AppUpdate;
using Xamarin.Google.Android.Play.Core.Install;
using Xamarin.Google.Android.Play.Core.Install.Model;
using Exception = System.Exception;
using Object = Java.Lang.Object;

namespace VpnHood.Client.App.Droid.GooglePlay;

public class GooglePlayAppUpdaterService : IAppUpdaterService
{
    public async Task<bool> Update(IUiContext uiContext)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        using var appUpdateManager = AppUpdateManagerFactory.Create(appUiContext.Activity);
        try
        {
            var appUpdateInfo = await new GooglePlayTaskCompleteListener<AppUpdateInfo>(appUpdateManager.AppUpdateInfo).Task.VhConfigureAwait();
            var updateAvailability = appUpdateInfo.UpdateAvailability();

            // play set UpdateAvailability.UpdateNotAvailable even when there is no connection to google
            // So we return false if there is UpdateNotAvailable to let the alternative way works
            if (updateAvailability != UpdateAvailability.UpdateAvailable || !appUpdateInfo.IsUpdateTypeAllowed(AppUpdateType.Flexible))
                return false;

            // Set download listener
            using var googlePlayDownloadStateListener = new GooglePlayDownloadCompleteListener(appUpdateManager);

            // Show Google Play update dialog
            var updateFlowPlayTask = appUpdateManager.StartUpdateFlow(appUpdateInfo, appUiContext.Activity, AppUpdateOptions.NewBuilder(AppUpdateType.Flexible).Build());
            var updateFlowResult = await new GooglePlayTaskCompleteListener<Integer>(updateFlowPlayTask).Task.VhConfigureAwait();
            if (updateFlowResult.IntValue() != -1)
                throw new Exception("Could not start update flow.");

            // Wait for download complete
            await googlePlayDownloadStateListener.WaitForCompletion().VhConfigureAwait();

            // Start install downloaded update
            var installUpdateTask = appUpdateManager.CompleteUpdate();
            var installUpdateStatus = await new GooglePlayTaskCompleteListener<Integer>(installUpdateTask).Task.VhConfigureAwait();

            // Could not start install
            if (installUpdateStatus.IntValue() != -1)
                throw new Exception("Could not complete update.");

            return true;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(ex, "Could update the app using Google Play.");
            return false;
        }
    }

    public class GooglePlayDownloadCompleteListener
        : Object, IInstallStateUpdatedListener
    {
        private readonly TaskCompletionSource _taskCompletionSource = new();
        private readonly IAppUpdateManager _appUpdateManager;

        public Task WaitForCompletion()
        {
            return _taskCompletionSource.Task;
        } 

        public  GooglePlayDownloadCompleteListener(IAppUpdateManager appUpdateManager)
        {
            appUpdateManager.RegisterListener(this);
            _appUpdateManager = appUpdateManager;
        }

        public void OnStateUpdate(InstallState state)
        {
            var status = state.InstallStatus();
            switch (status)
            {
                case InstallStatus.Installed:
                case InstallStatus.Downloaded:
                    _taskCompletionSource.TrySetResult();
                    break;

                case InstallStatus.Canceled:
                    _taskCompletionSource.SetCanceled();
                    break;

                case InstallStatus.Failed:
                    _taskCompletionSource.TrySetException(new Exception("Download failed."));
                    break;

                case InstallStatus.Unknown:
                    _taskCompletionSource.TrySetException(new Exception("Unknown status for download."));
                    break;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!_taskCompletionSource.Task.IsCompleted)
                    _taskCompletionSource.TrySetCanceled();

                _appUpdateManager.UnregisterListener(this);
            }

            base.Dispose(disposing);
        }
    }
}