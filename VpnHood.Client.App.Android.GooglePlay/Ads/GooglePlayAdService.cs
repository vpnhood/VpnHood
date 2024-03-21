
using Android.Gms.Ads;
using Android.Gms.Ads.Initialization;
using Android.Gms.Ads.Interstitial;
using VpnHood.Client.Device.Droid.Utils;

namespace VpnHood.Client.App.Droid.GooglePlay.Ads;
//[Activity(Label = "@string/ads_activity_title")]
public class GooglePlayAdService
{
    private static bool _isInitializedCalled;

    public static void InitAds(Activity activity, string adUnitId)
    {
        try
        {
            if (_isInitializedCalled)
            {
                MobileAds.Initialize(activity);
            }
            /*var adRequest = new AdRequest.Builder().Build();
            InterstitialAd.Load(activity, adUnitId, adRequest, new InterstitialAdLoadCallback2(activity));*/
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        
    }


    /*private static void InitAds2(Activity activity)
    {
        try
        {
            if (_isInitializedCalled)
            {
                MobileAds.Initialize(activity);
                _isInitializedCalled = true;
            }

            if (!VpnHoodApp.Instance.IsWaitingForAd)
            {
                VpnHoodApp.Instance.IsWaitingForAd = true;
                var adRequest = new AdRequest.Builder().Build();
                InterstitialAd.Load(activity, "ca-app-pub-3940256099942544/1033173712", adRequest,
                    new VpnHoodInterstitialAdLoadCallback(activity));
            }
        }
        catch
        {
            // ignored
            // Lucky at the moment
        }
    }*/
    
    private class MyOnInitializationCompleteListener: Java.Lang.Object, IOnInitializationCompleteListener
    {
        public void OnInitializationComplete(IInitializationStatus p0)
        {
           
        }

    }
}
