using Com.Unity3d.Ads;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Droid;

namespace VpnHood.Client.App.Android.Unity.Ads;

public class UnityAdService(string adGameId, string adPlacementId, bool isTestMode) : IAppAdService
{
    private DateTime _lastLoadRewardedAdTime = DateTime.MinValue;


    public static UnityAdService Create(string adGameId, string adPlacementId, bool isTestMode)
    {
        var ret = new UnityAdService(adGameId, adPlacementId, isTestMode);
        return ret;
    }

    public Task ShowAd(IUiContext uiContext, string customData, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        var activity = appUiContext.Activity;

        UnityAds.Initialize(appUiContext, adGameId, isTestMode, new AdInitializationListener(activity));
    }

    private class AdInitializationListener(Activity activity) : Java.Lang.Object, IUnityAdsInitializationListener
    {
        public void OnInitializationComplete()
        {
            DisplayRewardedAd();
        }

        public void OnInitializationFailed(UnityAds.UnityAdsInitializationError? error, string? message)
        {
            throw new NotImplementedException();
        }

        private void DisplayRewardedAd()
        {
            UnityAds.Load(adPlacementId, new AdLoadListener(activity));
        }
    }

    public class AdLoadListener(Activity currentActivity) : Java.Lang.Object, IUnityAdsLoadListener
    {
        public void OnUnityAdsAdLoaded(string? adPlacementId)
        {
            var adShowOption = new UnityAdsShowOptions();

            // TODO check with tutorial
            // https://docs.unity.com/ads/en-us/manual/ImplementingS2SRedeemCallbacks
            adShowOption.SetObjectId("your-side-id");

            UnityAds.Show(currentActivity, adPlacementId, adShowOption, new AdShowListener());
        }

        public void OnUnityAdsFailedToLoad(string? adUnitId, UnityAds.UnityAdsLoadError? error, string? message)
        {
            throw new NotImplementedException();
        }
    }

    public class AdShowListener() : Java.Lang.Object, IUnityAdsShowListener
    {
        public void OnUnityAdsShowClick(string? adPlacementId)
        {
            throw new NotImplementedException();
        }

        public void OnUnityAdsShowComplete(string? adPlacementId, UnityAds.UnityAdsShowCompletionState? state)
        {
            Console.WriteLine(
                state == UnityAds.UnityAdsShowCompletionState.Completed
                    ? "Unity Ads Show Completed"
                    : "Unity Ads Show Skipped");
        }

        public void OnUnityAdsShowFailure(string? adPlacementId, UnityAds.UnityAdsShowError? error, string? message)
        {
            throw new NotImplementedException();
        }

        public void OnUnityAdsShowStart(string? adPlacementId)
        {
            throw new NotImplementedException();
        }
    }
    public void Dispose()
    {
        throw new NotImplementedException();
    }
}