
using Android.Gms.Ads;
using Android.Gms.Ads.Initialization;
using Android.Gms.Ads.Interstitial;
using VpnHood.Client.Device.Droid.Utils;

namespace VpnHood.Client.App.Droid.GooglePlay.Ads;
//[Activity(Label = "@string/ads_activity_title")]
public class GooglePlayAdService
{

    public static void InitAds(Activity activity, string adUnitId)
    {
        try
        {
            MobileAds.Initialize(activity, new OnInitializationCompleteListener(activity, adUnitId));
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    
    private class OnInitializationCompleteListener(Activity activity, string adUnitId): Java.Lang.Object, IOnInitializationCompleteListener
    {
        public void OnInitializationComplete(IInitializationStatus p0)
        {
            var adRequest = new AdRequest.Builder().Build();
            InterstitialAd.Load(activity, adUnitId, adRequest, new CustomAdLoadCallback(activity));
        }
    }
}
