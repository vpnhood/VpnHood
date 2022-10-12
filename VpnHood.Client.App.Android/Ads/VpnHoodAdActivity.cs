#nullable enable
using Android.App;
using Android.Gms.Ads;
using Android.Gms.Ads.Interstitial;
using Android.OS;

namespace VpnHood.Client.App.Android.Ads;

[Activity(Label = "@string/ad")]
public class VpnHoodAdActivity : Activity
{
    private static bool _isInitializedCalled = false;
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
                InterstitialAd.Load(activity, "ca-app-pub-9339227682123409/2322872125", adRequest,
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