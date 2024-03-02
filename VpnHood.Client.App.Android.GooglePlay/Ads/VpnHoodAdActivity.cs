
using Android.Gms.Ads;
using Android.Gms.Ads.Interstitial;

namespace VpnHood.Client.App.Droid.GooglePlay.Ads;
//[Activity(Label = "@string/ads_activity_title")]
public class VpnHoodAdActivity : Activity
{
    private static bool _isInitializedCalled;
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        InitAds(this);
    }

    private static void InitAds(Activity activity)
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
                InterstitialAd.Load(activity, "ca-app-pub-8662231806304184/7575717622", adRequest,
                    new VpnHoodInterstitialAdLoadCallback(activity));
            }
        }
        catch
        {
            // ignored
            // Lucky at the moment
        }
    }
}
